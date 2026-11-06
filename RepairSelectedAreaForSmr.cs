using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace LinkArea2024
{
    internal sealed class RepairSelectedAreaForSmr
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument ui = data.Application.ActiveUIDocument;
                Document doc = ui.Document;
                ViewPlan view = doc.ActiveView as ViewPlan;
                if (view == null || view.ViewType != ViewType.AreaPlan)
                    throw new InvalidOperationException("Откройте план зонирования проблемной зоны.");
                Area area = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Area>().SingleOrDefault();
                if (area == null) area = doc.GetElement(ui.Selection.PickObject(ObjectType.Element, new AreaFilter(), "Выберите зону, границу которой исправить для СМР")) as Area;
                if (area.LevelId != view.GenLevel.Id || area.AreaScheme.Id != view.AreaScheme.Id)
                    throw new InvalidOperationException("Зона должна относиться к схеме и уровню активного плана.");
                List<Curve> curves;
                HashSet<ElementId> oldIds;
                using (var options = new SpatialElementBoundaryOptions())
                {
                    var loops = area.GetBoundarySegments(options);
                    if (loops == null || loops.Count != 1 || area.Area <= 0)
                        throw new InvalidOperationException("Нужна размещённая зона с одним наружным контуром.");
                    curves = loops[0].Select(s => s.GetCurve()).ToList();
                    oldIds = new HashSet<ElementId>(loops[0].Select(s => s.ElementId));
                    foreach (Area other in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Areas)
                        .WhereElementIsNotElementType().OfType<Area>())
                    {
                        if (other.Id == area.Id || other.LevelId != area.LevelId || other.AreaScheme.Id != area.AreaScheme.Id) continue;
                        var otherLoops = other.GetBoundarySegments(options);
                        if (otherLoops != null && otherLoops.SelectMany(l => l).Any(s => oldIds.Contains(s.ElementId)))
                            throw new InvalidOperationException("Граница общая с зоной " + other.Id + ". Автоматическая замена отменена.");
                    }
                }
                foreach (ElementId id in oldIds)
                {
                    Element e = doc.GetElement(id);
                    if (!(e is CurveElement) || e.Category == null || e.Category.Id.Value != (long)BuiltInCategory.OST_AreaSchemeLines)
                        throw new InvalidOperationException("Граница содержит элемент, который нельзя безопасно заменить: " + id);
                }
                var source = curves.SelectMany(c => c.Tessellate()).Select(p => new OutlinePoint(p.X, p.Y)).ToList();
                Curve longest = curves.OrderByDescending(c => c.Length).First();
                XYZ direction = longest.GetEndPoint(1) - longest.GetEndPoint(0);
                // The previous zone already includes its requested clearance; do not add another 20 mm.
                var outline = ContourCleaner.Rectangle(source, 0, Math.Atan2(direction.Y, direction.X), 1.0 / 304.8).Points;
                double elevation = view.GenLevel.ProjectElevation;
                foreach (CurveElement existing in new FilteredElementCollector(doc, view.Id).OfCategory(BuiltInCategory.OST_AreaSchemeLines).OfType<CurveElement>())
                {
                    if (oldIds.Contains(existing.Id)) continue;
                    var tess = existing.GeometryCurve.Tessellate();
                    for (int i = 0; i + 1 < tess.Count; i++)
                    {
                        var a = tess[i]; var b = tess[i + 1];
                        if (Math.Abs(a.Z - elevation) < .001 && EnclosingOutline.SegmentTouches(outline,
                            new OutlinePoint(a.X, a.Y), new OutlinePoint(b.X, b.Y), .01 / 304.8))
                            throw new InvalidOperationException("Новый прямоугольник захватывает другую границу: " + existing.Id + ". Замена отменена.");
                    }
                }
                var saved = area.Parameters.Cast<Parameter>().Where(p => p.StorageType == StorageType.String)
                    .ToDictionary(p => p.Id, p => p.AsString());
                var newIds = new HashSet<ElementId>();
                var failures = new RepairFailures();
                using (var group = new TransactionGroup(doc, "Исправить границу зоны для СМР"))
                {
                    group.Start();
                    using (var tx = new Transaction(doc, "Заменить границу четырьмя линиями"))
                    {
                        tx.Start();
                        tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true));
                        var removed = doc.Delete(oldIds.ToList());
                        if (removed.Any(id => !oldIds.Contains(id)))
                            throw new InvalidOperationException("Удаление границ затрагивает дополнительные элементы. Замена отменена, исходная модель восстановлена.");
                        var plane = SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, elevation)));
                        for (int i = 0; i < 4; i++)
                        {
                            var a = outline[i]; var b = outline[(i + 1) % 4];
                            newIds.Add(doc.Create.NewAreaBoundaryLine(plane, Line.CreateBound(new XYZ(a.X, a.Y, elevation), new XYZ(b.X, b.Y, elevation)), view).Id);
                        }
                        if (tx.Commit() != TransactionStatus.Committed)
                            throw new InvalidOperationException("Revit отменил замену: " + failures.Text);
                    }
                    if (!area.IsValidObject || area.Area <= 0) throw new InvalidOperationException("Исходная зона не сохранилась. Замена отменена.");
                    using (var options = new SpatialElementBoundaryOptions())
                    {
                        var loops = area.GetBoundarySegments(options);
                        if (loops == null || loops.Count != 1 || loops[0].Count != 4)
                            throw new InvalidOperationException("Revit не вернул ровно четыре участка. Замена отменена; исходная зона восстановлена.");
                        var pieces = loops[0].Select(s => s.GetCurve()).Select(c => new BoundaryPiece(
                            new OutlinePoint(c.GetEndPoint(0).X, c.GetEndPoint(0).Y), new OutlinePoint(c.GetEndPoint(1).X, c.GetEndPoint(1).Y))).ToList();
                        BoundaryVerifier.Verify(outline, pieces, .01 / 304.8);
                    }
                    foreach (Parameter p in area.Parameters)
                        if (saved.ContainsKey(p.Id) && saved[p.Id] != p.AsString())
                            throw new InvalidOperationException("Параметры зоны изменились. Замена отменена.");
                    group.Assimilate();
                }
                try { ui.Selection.SetElementIds(new[] { area.Id }); } catch { }
                TaskDialog.Show("Граница исправлена", "Зона " + area.Id + " сохранена вместе с текстовыми параметрами.\nФактическая граница: ровно четыре участка.\nПроверьте зону в плагине СМР. Отмена замены: Ctrl+Z.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { TaskDialog.Show("Исправление границы", ex.Message); return Result.Cancelled; }
        }
        private class AreaFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) { return e is Area; }
            public bool AllowReference(Reference r, XYZ p) { return false; }
        }
        private class RepairFailures : IFailuresPreprocessor
        {
            public string Text = "";
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                bool block = false;
                foreach (var failure in accessor.GetFailureMessages())
                {
                    var id = failure.GetFailureDefinitionId();
                    if (failure.GetSeverity() == FailureSeverity.Warning && id == BuiltInFailures.InaccurateFailures.InaccurateAreaLine)
                        accessor.DeleteWarning(failure);
                    else { Text += failure.GetDescriptionText() + "\n"; block = true; }
                }
                return block ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
            }
        }
    }
}

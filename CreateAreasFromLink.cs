using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace LinkArea2024
{
    internal sealed class CreateAreasFromLink
    {
        private const double Mm = 1.0 / 304.8;
        private readonly List<string> log = new List<string>();

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            log.Clear();
            try
            {
                UIDocument ui = data.Application.ActiveUIDocument;
                Document doc = ui.Document;
                ViewPlan view = doc.ActiveView as ViewPlan;
                if (view == null || view.ViewType != ViewType.AreaPlan || view.IsTemplate)
                    throw new InvalidOperationException("Откройте план зонирования нужного этажа и запустите команду ещё раз.");
                if (doc.IsReadOnly || doc.IsFamilyDocument)
                    throw new InvalidOperationException("Нужен доступный для редактирования проект Revit.");
                var selected = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<RevitLinkInstance>().ToList();
                RevitLinkInstance link = selected.Count == 1 ? selected[0] :
                    doc.GetElement(ui.Selection.PickObject(ObjectType.Element, new LinkFilter(),
                        "Выберите связь Revit, все модельные элементы которой нужно охватить одной зоной")) as RevitLinkInstance;
                Document source = link.GetLinkDocument();
                if (source == null) throw new InvalidOperationException("Выбранная связь выгружена. Загрузите её.");
                Transform transform = link.GetTotalTransform();
                if (Math.Abs(transform.BasisZ.DotProduct(XYZ.BasisZ) - 1) > 1e-8)
                    throw new InvalidOperationException("Поддерживаются горизонтальные связи без наклона и переворота по Z.");
                double elevation = view.GenLevel.ProjectElevation;
                bool placeTag;
                bool includeNested;
                bool simpleContour;
                double marginMm;
                SmrValues smr;
                using (var picker = new BuildOptions(elevation, view.GenLevel.Name, link.Name))
                {
                    if (picker.ShowDialog(new RevitWindow(data.Application.MainWindowHandle)) != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;
                    placeTag = picker.PlaceTag;
                    includeNested = picker.IncludeNested;
                    simpleContour = picker.SimpleContour;
                    marginMm = picker.MarginMm;
                    smr = picker.Smr;
                }
                var points = new List<OutlinePoint>();
                var bounds = new BoundsSummary();
                CollectBounds(source, transform, points, bounds, new HashSet<Document>(), includeNested);
                if (points.Count < 3)
                    throw new InvalidOperationException("В выбранной связи не найдены физические тела или сетки.");
                log.Add("Связь: " + link.Name + "; экземпляр: " + link.Id + "; элементов с физической геометрией: " + bounds.Elements);
                log.Add("Трансформация экземпляра: начало " + transform.Origin + "; X " + transform.BasisX + "; Y " + transform.BasisY);
                log.Add("Все этажи, стадии и варианты; отметка плана: " + elevation + "; контуры проецируются на план по Z.");
                log.Add("Без габаритов: " + bounds.WithoutBounds + "; выгруженных вложенных связей: " + bounds.UnloadedLinks);
                log.Add("Включать вложенные связи: " + includeNested + "; исключено экземпляров вложенных связей: " + bounds.ExcludedLinks);
                foreach (var extent in bounds.Extents.OrderBy(e => e.Left).Take(3)) log.Add("Край X min: " + extent);
                foreach (var extent in bounds.Extents.OrderByDescending(e => e.Right).Take(3)) log.Add("Край X max: " + extent);
                foreach (var extent in bounds.Extents.OrderBy(e => e.Bottom).Take(3)) log.Add("Край Y min: " + extent);
                foreach (var extent in bounds.Extents.OrderByDescending(e => e.Top).Take(3)) log.Add("Край Y max: " + extent);
                foreach (var extent in bounds.Extents.OrderByDescending(e => e.RawExcess).Take(5).Where(e => e.RawExcess > Mm))
                    log.Add("Отброшенный излишек общего габарита: " + extent + "; излишек по размеру XY: " + (extent.RawExcess * .3048).ToString("F2") + " м");
                double shortTolerance = data.Application.Application.ShortCurveTolerance;
                double minimumEdge = Math.Max(10 * Mm, shortTolerance * 2);
                var clean = simpleContour ? ContourCleaner.Rectangle(points, marginMm * Mm,
                    Math.Atan2(transform.BasisX.Y, transform.BasisX.X), Mm) :
                    ContourCleaner.Build(points, marginMm * Mm, minimumEdge, .1);
                var outline = clean.Points;
                ContourCleaner.Validate(outline, minimumEdge, .1);
                log.Add("Простой контур из четырёх линий: " + simpleContour);
                log.Add("Очистка контура: вершин " + clean.OriginalVertices + " -> " + outline.Count +
                    "; компенсация наружу " + (clean.ExtraOutward / Mm).ToString("F3") + " мм; минимальное ребро " + (minimumEdge / Mm) + " мм.");
                if (!EnclosingOutline.ContainsAll(outline, points, .1 * Mm))
                    throw new InvalidOperationException("Проверка охвата не пройдена: часть габаритов осталась вне контура. Изменения не внесены.");
                log.Add("Проверка охвата всех " + points.Count + " углов габаритов пройдена (допуск 0.1 мм).");
                var enclosing = new CurveLoop();
                for (int i = 0; i < outline.Count; i++)
                {
                    var a = outline[i];
                    var b = outline[(i + 1) % outline.Count];
                    if (Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)) < data.Application.Application.ShortCurveTolerance)
                        throw new InvalidOperationException("На внешнем контуре получилась слишком короткая линия. Увеличьте отступ наружу.");
                    enclosing.Append(Line.CreateBound(new XYZ(a.X, a.Y, elevation), new XYZ(b.X, b.Y, elevation)));
                }
                var contours = new List<CurveLoop> { enclosing };
                log.Add("Одна выпуклая огибающая; отступ: " + marginMm + " мм; марка: " + placeTag);

                var regions = contours.Select(loop => GeometryCreationUtilities.CreateExtrusionGeometry(new[] { loop }, XYZ.BasisZ, 1))
                    .Select(s => s.Faces.Cast<Face>().OfType<PlanarFace>().First(f => f.FaceNormal.Z > .999999)).ToList();
                // Never add overlapping boundaries or split someone else's existing areas.
                var existing = new FilteredElementCollector(doc, view.Id).OfCategory(BuiltInCategory.OST_AreaSchemeLines)
                    .WhereElementIsNotElementType().OfType<CurveElement>().ToList();
                foreach (CurveElement line in existing)
                {
                    Curve curve = line.GeometryCurve;
                    XYZ start = curve.GetEndPoint(0), end = curve.GetEndPoint(1);
                    // Underlays can display boundary lines at a different elevation.
                    if (Math.Abs(start.Z - elevation) > Mm || Math.Abs(end.Z - elevation) > Mm) continue;
                    bool conflict;
                    if (curve is Line)
                        conflict = EnclosingOutline.SegmentTouches(outline, new OutlinePoint(start.X, start.Y),
                            new OutlinePoint(end.X, end.Y), .1 * Mm);
                    else
                    {
                        XYZ mid = curve.Evaluate(.5, true);
                        conflict = new[] { start, mid, end }.Any(p =>
                            EnclosingOutline.ContainsAll(outline, new[] { new OutlinePoint(p.X, p.Y) }, .1 * Mm)) ||
                            enclosing.Any(edge => curve.Intersect(edge) != SetComparisonResult.Disjoint);
                    }
                    if (conflict)
                    {
                        double width = (outline.Max(p => p.X) - outline.Min(p => p.X)) * .3048;
                        double height = (outline.Max(p => p.Y) - outline.Min(p => p.Y)) * .3048;
                        log.Add("Конфликт: линия " + line.Id + "; начало " + start + "; конец " + end + "; размер контура XY " + width + " x " + height + " м.");
                        try { ui.Selection.SetElementIds(new[] { line.Id }); ui.ShowElements(line.Id); } catch { }
                        throw new InvalidOperationException("Реальное пересечение нового контура с границей зон в КООРДИНАЦИОННОМ файле: линия " + line.Id +
                            ". Это не означает, что вы уже создавали зоны на выбранной связи.\n\nКонфликтующая линия выделена. Габарит нового контура по XY: " +
                            width.ToString("F1") + " × " + height.ToString("F1") + " м.\nПроверьте выбранную связь и отступ. Существующие элементы не удалены, новые не созданы.");
                    }
                }

                int lineCount = 0, areaCount = 0, tags = 0;
                var newIds = new List<ElementId>();
                var createdAreas = new List<Area>();
                var failures = new BoundaryFailures(log, newIds);
                using (var group = new TransactionGroup(doc, "Зоны по контуру связи"))
                {
                    group.Start();
                    using (var tx = new Transaction(doc, "Границы и зоны"))
                    {
                        tx.Start();
                        failures.BeginStage("Границы зон");
                        tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true));
                        SketchPlane plane = SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, elevation)));
                        foreach (CurveLoop contour in contours)
                            foreach (Curve edge in contour)
                            {
                                ElementId boundaryId = doc.Create.NewAreaBoundaryLine(plane, edge, view).Id;
                                newIds.Add(boundaryId);
                                lineCount++;
                            }
                        doc.Regenerate();
                        if (tx.Commit() != TransactionStatus.Committed)
                            throw new InvalidOperationException("Создание границ отменено.\n" + failures.BlockingText);
                    }
                    using (var tx = new Transaction(doc, "Размещение зон"))
                    {
                        tx.Start();
                        failures.BeginStage("Размещение зон");
                        tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true));
                        bool hasTag = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol))
                            .OfCategory(BuiltInCategory.OST_AreaTags).Any();
                        for (int i = 0; i < contours.Count; i++)
                        {
                            XYZ seed = InteriorPoint(regions[i]);
                            var uv = new UV(seed.X, seed.Y);
                            Area area = doc.Create.NewArea(view, uv);
                            doc.Regenerate();
                            if (area == null) throw new InvalidOperationException("Revit не создал зону. Изменения отменены.");
                            if (smr != null) FillSmrParameters(area, smr);
                            Parameter name = area.get_Parameter(BuiltInParameter.ROOM_NAME);
                            if (name != null && !name.IsReadOnly) name.Set("Зона здания — " + source.Title);
                            newIds.Add(area.Id);
                            createdAreas.Add(area);
                            areaCount++;
                            if (placeTag && hasTag)
                            {
                                using (var sub = new SubTransaction(doc))
                                {
                                    sub.Start();
                                    try { doc.Create.NewAreaTag(view, area, uv); sub.Commit(); tags++; }
                                    catch { sub.RollBack(); }
                                }
                            }
                        }
                        if (tx.Commit() != TransactionStatus.Committed)
                            throw new InvalidOperationException("Создание зоны отменено; границы также отменены.\n" + failures.BlockingText);
                    }
                    // Area computation is checked after transaction commit, while the group can still roll back.
                    for (int i = 0; i < createdAreas.Count; i++)
                    {
                        Area area = createdAreas[i];
                        if (smr != null)
                            foreach (var pair in smr.Parameters())
                                if (area.GetParameters(pair.Key).Single().AsString() != pair.Value)
                                    throw new InvalidOperationException("После сохранения изменилось значение «" + pair.Key + "». Создание зоны и границ отменено.");
                        double expected = regions[i].Area;
                        if (area.Area <= 0 || Math.Abs(area.Area - expected) > Math.Max(expected * .001, .01))
                            throw new InvalidOperationException("Зона не замкнулась или её площадь отличается от контура. Возможны существующие границы/зоны. Все созданные элементы отменены.");
                        ValidateActualBoundary(area, outline, points, elevation, minimumEdge);
                        log.Add("Зона " + area.Id + ": " + (area.Area * .09290304).ToString("F2", CultureInfo.InvariantCulture) + " м²");
                    }
                    if (group.Assimilate() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Не удалось завершить операцию.");
                }
                try { ui.Selection.SetElementIds(newIds); } catch { /* Selection is optional after successful creation. */ }
                string summary = "Готово.\nВсе этажи связи: " + link.Name +
                    "\nЭлементов с физической геометрией: " + bounds.Elements + "\nЛиний границы: " + lineCount + "\nЗон: " + areaCount + "\nМарок зон: " + tags +
                    "\n\nВсе учтённые габариты внутри контура. Отступ наружу: " + marginMm + " мм.\nОтмена всей операции: Ctrl+Z." +
                    (bounds.WithoutBounds > 0 ? "\nЭлементов без физических тел/сеток (не расширяют контур): " + bounds.WithoutBounds : "") +
                    (bounds.UnloadedLinks > 0 ? "\nНе учтены выгруженные вложенные связи: " + bounds.UnloadedLinks + ". Загрузите их и перестройте зону." : "") +
                    (placeTag && tags < areaCount ? "\nМарка не установлена. Загрузите семейство марки зоны и расставьте марку штатной командой Revit." : "") +
                    (failures.AcceptedNotes.Count > 0 ? "\nПредупреждений о небольшом отклонении линий от осей: " + failures.AcceptedNotes.Count + ". Подробности — в развёрнутом сообщении." : "");
                if (smr != null) summary += "\n\nСМР: " + smr.Block + "; " + smr.Section + "\nОписание: " + smr.Description;
                summary += "\nФактический контур проверен: один замкнутый цикл без коротких рёбер и самопересечений.";
                var resultDialog = new TaskDialog("LinkArea — зона создана") { MainContent = summary };
                if (failures.AcceptedNotes.Count > 0) resultDialog.ExpandedContent = string.Join("\n\n", failures.AcceptedNotes);
                resultDialog.Show();
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.Message;
                log.Add(ex.ToString());
                TaskDialog.Show("LinkArea2024 — операция не выполнена", ex.Message);
                return Result.Cancelled;
            }
            finally { WriteLog(); }
        }

        private void ValidateActualBoundary(Area area, List<OutlinePoint> expected, List<OutlinePoint> sourcePoints,
            double elevation, double minimumEdge)
        {
            using (var options = new SpatialElementBoundaryOptions())
            {
                var loops = area.GetBoundarySegments(options);
                if (loops == null || loops.Count != 1 || loops[0].Count < 3)
                    throw new InvalidOperationException("Revit вернул несколько контуров или пустую границу зоны. Создание отменено.");
                var pieces = new List<BoundaryPiece>();
                for (int i = 0; i < loops[0].Count; i++)
                {
                    var segment = loops[0][i];
                    Curve curve = segment.GetCurve();
                    if (!(curve is Line)) throw new InvalidOperationException("Фактическая граница содержит непрямолинейный участок. Создание отменено.");
                    XYZ a = curve.GetEndPoint(0), b = curve.GetEndPoint(1);
                    if (Math.Abs(a.Z - elevation) > .01 * Mm || Math.Abs(b.Z - elevation) > .01 * Mm)
                        throw new InvalidOperationException("Граница зоны вышла из плоскости уровня. Создание отменено.");
                    pieces.Add(new BoundaryPiece(new OutlinePoint(a.X, a.Y), new OutlinePoint(b.X, b.Y)));
                    log.Add("Участок границы: ID " + segment.ElementId + "; " + a + " -> " + b);
                }
                var actual = BoundaryVerifier.Verify(expected, pieces, .01 * Mm);
                ContourCleaner.Validate(actual, minimumEdge, .1);
                double signedArea = 0;
                var origin = actual[0];
                for (int i = 0; i < actual.Count; i++)
                {
                    var a = actual[i];
                    var b = actual[(i + 1) % actual.Count];
                    signedArea += (a.X - origin.X) * (b.Y - origin.Y) - (a.Y - origin.Y) * (b.X - origin.X);
                }
                if (signedArea < 0) actual.Reverse();
                if (!EnclosingOutline.ContainsAll(actual, sourcePoints, .1 * Mm))
                    throw new InvalidOperationException("Фактическая граница Revit не охватывает всю собранную геометрию. Создание отменено.");
                log.Add("Проверена фактическая граница зоны " + area.Id + ": один цикл; углов " + actual.Count + "; участков Revit " + pieces.Count + "; допуск стыков/покрытия 0.01 мм.");
            }
        }

        private void FillSmrParameters(Area area, SmrValues values)
        {
            var errors = new List<string>();
            foreach (var pair in values.Parameters())
            {
                var matches = area.GetParameters(pair.Key);
                if (matches.Count == 0) errors.Add("Нет параметра: " + pair.Key);
                else if (matches.Count != 1) errors.Add("Несколько параметров с именем: " + pair.Key);
                else if (matches[0].IsReadOnly) errors.Add("Параметр только для чтения: " + pair.Key);
                else if (matches[0].StorageType != StorageType.String) errors.Add("Параметр должен быть текстовым: " + pair.Key);
            }
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join("\n", errors) +
                    "\n\nСоздание зоны и границ отменено. Если параметры отсутствуют, откройте штатный плагин «Построить объемы СМР» и нажмите отмену, чтобы загрузить параметры. Либо выключите заполнение СМР в этой команде.");
            foreach (var pair in values.Parameters())
            {
                if (!area.GetParameters(pair.Key).Single().Set(pair.Value))
                    throw new InvalidOperationException("Revit не записал параметр «" + pair.Key + "». Создание зоны и границ отменено.");
                log.Add("Зона " + area.Id + "; " + pair.Key + " = " + pair.Value);
            }
        }

        private sealed class BoundsSummary
        {
            public int Elements, WithoutBounds, UnloadedLinks, ExcludedLinks;
            public readonly List<PhysicalExtent> Extents = new List<PhysicalExtent>();
        }

        private sealed class PhysicalExtent
        {
            public double Left, Right, Bottom, Top, RawExcess;
            public string Description;
            public override string ToString()
            {
                return Description + "; физический XY, м: [" + (Left * .3048).ToString("F2") + ", " +
                    (Bottom * .3048).ToString("F2") + "] — [" + (Right * .3048).ToString("F2") + ", " +
                    (Top * .3048).ToString("F2") + "]";
            }
        }

        private void CollectBounds(Document source, Transform toHost, List<OutlinePoint> points,
            BoundsSummary summary, HashSet<Document> route, bool includeNested)
        {
            if (!route.Add(source)) throw new InvalidOperationException("Циклическая вложенная связь: " + source.Title);
            try
            {
                foreach (Element element in new FilteredElementCollector(source).WhereElementIsNotElementType())
                {
                    if (element is RevitLinkInstance nested)
                    {
                        if (!includeNested) { summary.ExcludedLinks++; continue; }
                        Document nestedDoc = nested.GetLinkDocument();
                        if (nestedDoc == null)
                        {
                            summary.UnloadedLinks++;
                            log.Add("Выгруженная вложенная связь: " + nested.Name);
                        }
                        else CollectBounds(nestedDoc, toHost.Multiply(nested.GetTotalTransform()), points, summary, route, true);
                        continue;
                    }
                    Category category = element.Category;
                    if (element.ViewSpecific || category == null || category.CategoryType != CategoryType.Model || category.IsTagCategory ||
                        element is DatumPlane || element is BasePoint || element is SpatialElement ||
                        element is CurveElement || element is Group || element is AssemblyInstance) continue;
                    int before = points.Count;
                    try
                    {
                        var options = new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = false };
                        CollectPhysicalGeometry(element.get_Geometry(options), toHost, points);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("Не удалось прочитать физическую геометрию: " + source.Title +
                            ", элемент " + element.Id + " (" + element.Name + "). " + ex.Message);
                    }
                    if (points.Count == before) { summary.WithoutBounds++; continue; }
                    var extent = new PhysicalExtent
                    {
                        Left = double.PositiveInfinity, Right = double.NegativeInfinity,
                        Bottom = double.PositiveInfinity, Top = double.NegativeInfinity,
                        Description = source.Title + "; ID " + element.Id + "; " + category.Name + "; " + element.Name
                    };
                    for (int i = before; i < points.Count; i++)
                    {
                        extent.Left = Math.Min(extent.Left, points[i].X);
                        extent.Right = Math.Max(extent.Right, points[i].X);
                        extent.Bottom = Math.Min(extent.Bottom, points[i].Y);
                        extent.Top = Math.Max(extent.Top, points[i].Y);
                    }
                    // The element-wide box is diagnostic only; it never contributes to the outline.
                    BoundingBoxXYZ raw = element.get_BoundingBox(null);
                    if (raw != null)
                    {
                        var rawPoints = new List<OutlinePoint>();
                        AddBox(raw, toHost, rawPoints);
                        extent.RawExcess = Math.Max(rawPoints.Max(p => p.X) - rawPoints.Min(p => p.X) - (extent.Right - extent.Left),
                            rawPoints.Max(p => p.Y) - rawPoints.Min(p => p.Y) - (extent.Top - extent.Bottom));
                    }
                    summary.Extents.Add(extent);
                    summary.Elements++;
                }
            }
            finally { route.Remove(source); }
        }

        private static void CollectPhysicalGeometry(GeometryElement geometry, Transform toHost, List<OutlinePoint> points)
        {
            if (geometry == null) return;
            foreach (GeometryObject obj in geometry)
            {
                if (obj is GeometryInstance instance)
                {
                    CollectPhysicalGeometry(instance.GetSymbolGeometry(), toHost.Multiply(instance.Transform), points);
                }
                else if (obj.Visibility != Visibility.Invisible && obj is Solid solid &&
                    solid.Faces.Size > 0 && Math.Abs(solid.Volume) > 1e-9)
                {
                    AddBox(solid.GetBoundingBox(), toHost, points);
                }
                else if (obj.Visibility != Visibility.Invisible && obj is Mesh mesh && mesh.NumTriangles > 0)
                {
                    foreach (XYZ vertex in mesh.Vertices)
                    {
                        XYZ point = toHost.OfPoint(vertex);
                        points.Add(new OutlinePoint(point.X, point.Y));
                    }
                }
                // Curves, points, controls, reference and symbolic lines cannot enlarge the contour.
            }
        }

        private static void AddBox(BoundingBoxXYZ box, Transform toHost, List<OutlinePoint> points)
        {
            Transform boxToHost = toHost.Multiply(box.Transform);
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                    {
                        XYZ p = boxToHost.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X,
                            y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z));
                        points.Add(new OutlinePoint(p.X, p.Y));
                    }
        }

        private static double Distance(OutlinePoint a, OutlinePoint b)
        {
            return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }

        private static XYZ InteriorPoint(PlanarFace face)
        {
            Mesh mesh = face.Triangulate();
            XYZ best = null;
            double clearance = -1;
            var edges = face.GetEdgesAsCurveLoops().SelectMany(l => l).ToList();
            for (int i = 0; i < mesh.NumTriangles; i++)
            {
                MeshTriangle tri = mesh.get_Triangle(i);
                XYZ p = (tri.get_Vertex(0) + tri.get_Vertex(1) + tri.get_Vertex(2)) / 3;
                var projected = face.Project(p);
                if (projected == null || !face.IsInside(projected.UVPoint)) continue;
                double distance = edges.Min(e => e.Distance(p));
                if (distance > clearance) { clearance = distance; best = p; }
            }
            if (best == null || clearance < Mm)
                throw new InvalidOperationException("Не найдена внутренняя точка зоны с отступом 1 мм от границы.");
            return best;
        }

        private void WriteLog()
        {
            try
            {
                string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "A101", "LinkArea2024", "Reports");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".txt"), log);
            }
            catch { /* A report failure must not undo successful model work. */ }
        }

        private class LinkFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) { return e is RevitLinkInstance; }
            public bool AllowReference(Reference r, XYZ p) { return false; }
        }

        private class BoundaryFailures : IFailuresPreprocessor
        {
            private readonly List<string> report;
            private readonly List<ElementId> created;
            private readonly List<string> blocking = new List<string>();
            private string stage;
            public readonly List<string> AcceptedNotes = new List<string>();
            public string BlockingText { get { return blocking.Count == 0 ?
                "Revit не вернул описание причины. Проверьте права редактирования и журнал Revit." : string.Join("\n\n", blocking.Take(8)); } }

            public BoundaryFailures(List<string> report, List<ElementId> created)
            {
                this.report = report;
                this.created = created;
            }

            public void BeginStage(string name) { stage = name; blocking.Clear(); }

            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                bool mustRollBack = false;
                foreach (FailureMessageAccessor failure in accessor.GetFailureMessages())
                {
                    FailureDefinitionId id = failure.GetFailureDefinitionId();
                    var affected = failure.GetFailingElementIds();
                    string detail = stage + ": " + failure.GetDescriptionText() +
                        "\nЭлементы: " + string.Join(", ", affected.Select(e => e.Value.ToString())) +
                        "\nКод: " + id.Guid;
                    bool isOffAxis = id == BuiltInFailures.InaccurateFailures.InaccurateAreaLine ||
                        id == BuiltInFailures.InaccurateFailures.InaccurateLine ||
                        id == BuiltInFailures.InaccurateFailures.InaccurateSketchLine;
                    // Permit only known orientation warnings affecting our newly created lines.
                    // Do not snap geometry, suppress unknown warnings, overlaps, or area failures.
                    bool permit = failure.GetSeverity() == FailureSeverity.Warning && isOffAxis &&
                        affected.Count > 0 && affected.All(e => created.Contains(e));
                    report.Add((permit ? "Допущено информационное предупреждение: " : "Откат: ") + detail);
                    if (permit)
                    {
                        if (!AcceptedNotes.Contains(detail)) AcceptedNotes.Add(detail);
                        accessor.DeleteWarning(failure);
                    }
                    else
                    {
                        if (!blocking.Contains(detail)) blocking.Add(detail);
                        mustRollBack = true;
                    }
                }
                return mustRollBack ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
            }
        }
    }
}

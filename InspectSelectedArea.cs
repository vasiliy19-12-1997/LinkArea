using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace LinkArea2024
{
    internal sealed class InspectSelectedArea
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument ui = data.Application.ActiveUIDocument;
                Document doc = ui.Document;
                Area area = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Area>().SingleOrDefault();
                if (area == null) area = doc.GetElement(ui.Selection.PickObject(ObjectType.Element, new AreaFilter(), "Выберите проблемную зону для отчёта")) as Area;
                var text = new StringBuilder();
                text.AppendLine("LinkArea Universal 2.0 — фактическая граница выбранной зоны (без изменений модели)");
                text.AppendLine("Revit: " + data.Application.Application.VersionNumber);
                text.AppendLine("Проект: " + doc.Title + "; вид: " + doc.ActiveView.Name + "; зона: " + area.Id + "; имя: " + area.Name);
                text.AppendLine("Схема: " + area.AreaScheme.Name + "; уровень: " + area.LevelId + "; площадь, ft²: " + R(area.Area));
                var level = doc.GetElement(area.LevelId) as Level;
                if (level != null) text.AppendLine("Отметки уровня: Elevation=" + R(level.Elevation) + "; ProjectElevation=" + R(level.ProjectElevation));
                if (area.Location is LocationPoint location) text.AppendLine("Точка зоны: " + Point(location.Point));
                foreach (string name in new[] { "ФОП_Блок СМР", "ФОП_Секция СМР", "ФОП_Описание СМР" })
                    text.AppendLine(name + "=" + string.Join(" | ", area.GetParameters(name).Select(p => p.AsString())));
                using (var options = new SpatialElementBoundaryOptions())
                {
                    var loops = area.GetBoundarySegments(options);
                    text.AppendLine("Циклов: " + (loops == null ? 0 : loops.Count));
                    if (loops != null)
                        for (int k = 0; k < loops.Count; k++)
                        {
                            text.AppendLine("Цикл " + k + "; участков: " + loops[k].Count);
                            for (int i = 0; i < loops[k].Count; i++)
                            {
                                var segment = loops[k][i];
                                Curve curve = segment.GetCurve();
                                Element boundary = doc.GetElement(segment.ElementId);
                                text.AppendLine("Участок " + i + "; ID=" + segment.ElementId + "; кривая=" + curve.GetType().FullName +
                                    "; элемент=" + boundary?.GetType().FullName + "; категория=" + boundary?.Category?.Name);
                                text.AppendLine("Начало=" + Point(curve.GetEndPoint(0)) + "; конец=" + Point(curve.GetEndPoint(1)) + "; длина ft=" + R(curve.Length));
                                Curve next = loops[k][(i + 1) % loops[k].Count].GetCurve();
                                text.AppendLine("Стык с следующим участком, мм=" + R(curve.GetEndPoint(1).DistanceTo(next.GetEndPoint(0)) * 304.8));
                                if (boundary is CurveElement ce)
                                {
                                    Plane plane = ce.SketchPlane.GetPlane();
                                    text.AppendLine("Плоскость: начало=" + Point(plane.Origin) + "; нормаль=" + Point(plane.Normal));
                                }
                            }
                        }
                }
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "A101", "LinkArea2024", "Reports");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "Area-" + area.Id + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                File.WriteAllText(path, text.ToString(), Encoding.UTF8);
                TaskDialog.Show("Отчёт границы зоны", "Модель не изменена.\nОтчёт сохранён:\n" + path);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { TaskDialog.Show("Отчёт зоны", ex.Message); return Result.Cancelled; }
        }

        private static string R(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        private static string Point(XYZ p) { return "(" + R(p.X) + ", " + R(p.Y) + ", " + R(p.Z) + ")"; }
        private sealed class AreaFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) { return element is Area; }
            public bool AllowReference(Reference reference, XYZ point) { return false; }
        }
    }
}

using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace LinkArea2024
{
    [Transaction(TransactionMode.Manual)]
    public sealed class UniversalCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument ui = data.Application.ActiveUIDocument;
                if (ui == null) throw new InvalidOperationException("Откройте проект и план зонирования.");
                var selected = ui.Selection.GetElementIds().Select(ui.Document.GetElement)
                    .Where(e => e is Area || e is RevitLinkInstance).ToList();
                if (selected.Count > 1)
                    throw new InvalidOperationException("Выделите одну связь или одну зону.");
                Element target = selected.SingleOrDefault();
                if (target == null)
                    target = ui.Document.GetElement(ui.Selection.PickObject(ObjectType.Element, new TargetFilter(),
                        "Выберите связь для создания зоны или существующую зону для исправления/отчёта"));
                ui.Selection.SetElementIds(new[] { target.Id });
                if (target is RevitLinkInstance)
                    return new CreateAreasFromLink().Execute(data, ref message, elements);

                var dialog = new TaskDialog("LinkArea — выбранная зона " + target.Id)
                {
                    MainInstruction = "Действие с выбранной зоной",
                    MainContent = "Исправление заменяет границу четырьмя линиями, сохраняя зону и параметры СМР. Отчёт только читает модель.",
                    CommonButtons = TaskDialogCommonButtons.Cancel
                };
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Исправить границу для СМР");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Сохранить диагностический отчёт");
                var action = dialog.Show();
                if (action == TaskDialogResult.CommandLink1)
                    return new RepairSelectedAreaForSmr().Execute(data, ref message, elements);
                if (action == TaskDialogResult.CommandLink2)
                    return new InspectSelectedArea().Execute(data, ref message, elements);
                return Result.Cancelled;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { TaskDialog.Show("LinkArea", ex.Message); return Result.Cancelled; }
        }

        private sealed class TargetFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) { return element is Area || element is RevitLinkInstance; }
            public bool AllowReference(Reference reference, XYZ point) { return false; }
        }
    }
}

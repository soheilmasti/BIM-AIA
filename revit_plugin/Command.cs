using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM_AIA.Handlers;
using BIM_AIA.Views;

namespace BIM_AIA
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CommandShowDockablePane : IExternalCommand
    {
        private static MainWindow? _activeWindow;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                if (uidoc == null || uidoc.Document == null)
                {
                    TaskDialog.Show("BIM AIA", "Please open a project model in Revit first.");
                    return Result.Cancelled;
                }

                if (_activeWindow != null && _activeWindow.IsLoaded)
                {
                    _activeWindow.WindowState = System.Windows.WindowState.Normal;
                    _activeWindow.Activate();
                    _activeWindow.Focus();
                    return Result.Succeeded;
                }

                var eventHandler = new BIMAIATransactionHandler();
                var externalEvent = ExternalEvent.Create(eventHandler);

                _activeWindow = new MainWindow(uidoc, externalEvent, eventHandler);

                var helper = new WindowInteropHelper(_activeWindow);
                helper.Owner = commandData.Application.MainWindowHandle;
                _activeWindow.Topmost = true;

                _activeWindow.Closed += (s, e) => _activeWindow = null;
                _activeWindow.Show();
                _activeWindow.Activate();
                _activeWindow.Focus();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("BIM AIA Error", $"Error launching BIM AIA window: {ex.Message}");
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CommandInspectChanges : IExternalCommand
    {
        private static AuditChangesWindow? _auditWindow;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                if (uidoc == null || uidoc.Document == null)
                {
                    TaskDialog.Show("BIM AIA", "Please open a project model in Revit first.");
                    return Result.Cancelled;
                }

                if (_auditWindow != null && _auditWindow.IsLoaded)
                {
                    _auditWindow.WindowState = System.Windows.WindowState.Normal;
                    _auditWindow.Activate();
                    _auditWindow.Focus();
                    return Result.Succeeded;
                }

                var eventHandler = new BIMAIATransactionHandler();
                var externalEvent = ExternalEvent.Create(eventHandler);

                _auditWindow = new AuditChangesWindow(uidoc, externalEvent, eventHandler);

                var helper = new WindowInteropHelper(_auditWindow);
                helper.Owner = commandData.Application.MainWindowHandle;
                _auditWindow.Topmost = true;

                _auditWindow.Closed += (s, e) => _auditWindow = null;
                _auditWindow.Show();
                _auditWindow.Activate();
                _auditWindow.Focus();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("BIM AIA Error", $"Error launching visual audit window: {ex.Message}");
                return Result.Failed;
            }
        }
    }
}

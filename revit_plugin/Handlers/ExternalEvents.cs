using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BIM_AIA.Handlers
{
    /// <summary>
    /// Preprocessor that automatically dismisses or resolves non-fatal Revit warnings,
    /// preventing modal dialog popups from freezing the user interface during AI operations.
    /// </summary>
    public class BIMAIASilentFailurePreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> failures = failuresAccessor.GetFailureMessages();
            foreach (FailureMessageAccessor failure in failures)
            {
                FailureSeverity severity = failure.GetSeverity();
                if (severity == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(failure);
                }
                else if (severity == FailureSeverity.Error)
                {
                    if (failure.HasResolutions())
                    {
                        failuresAccessor.ResolveFailure(failure);
                        return FailureProcessingResult.ProceedWithCommit;
                    }
                }
            }
            return FailureProcessingResult.Continue;
        }
    }

    /// <summary>
    /// مدیریت تراکنش‌های ایمن رویت از طریق الگوی ExternalEvent و TransactionGroup
    /// جهت اطمینان از قابلیت بازگشت کامل (Ctrl+Z) و جلوگیری از فریز یا کرش کردن نرم‌افزار.
    /// </summary>
    public class BIMAIATransactionHandler : IExternalEventHandler
    {
        public Action<UIApplication>? ActionToExecute { get; set; }
        public string TransactionTitle { get; set; } = "BIM AIA: Smart Action";
        public Action<bool, string>? OnExecutionFinished { get; set; }

        public void Execute(UIApplication uiapp)
        {
            if (ActionToExecute == null) return;

            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null || uidoc.Document == null)
            {
                OnExecutionFinished?.Invoke(false, "No active document open in Revit.");
                return;
            }

            Document doc = uidoc.Document;

            using (TransactionGroup tg = new TransactionGroup(doc, TransactionTitle))
            {
                try
                {
                    tg.Start();
                    // Execute directly on Revit main thread
                    ActionToExecute.Invoke(uiapp);
                    
                    if (tg.HasStarted())
                    {
                        tg.Assimilate(); // Combine all inner transactions into a single clean Undo step
                    }

                    OnExecutionFinished?.Invoke(true, $"Operation '{TransactionTitle}' successfully committed to model.");
                }
                catch (Exception ex)
                {
                    if (tg.HasStarted())
                    {
                        tg.RollBack();
                    }
                    // Trigger real-time autonomous self-healing engine
                    _ = Services.RevitSelfHealingEngine.DiagnoseAndHealAsync(uidoc, ex, TransactionTitle);
                    OnExecutionFinished?.Invoke(false, $"🛡️ Model exception handled by AI self-healing: {ex.Message}");
                }
                finally
                {
                    ActionToExecute = null;
                }
            }
        }

        public string GetName()
        {
            return "BIM AIA External Event Handler";
        }
    }
}

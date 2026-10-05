using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM_AIA.Handlers;

namespace BIM_AIA.Views
{
    public partial class AuditChangesWindow : Window
    {
        private readonly UIDocument _uidoc;
        private readonly ExternalEvent _externalEvent;
        private readonly BIMAIATransactionHandler _handler;

        public AuditChangesWindow(UIDocument uidoc, ExternalEvent externalEvent, BIMAIATransactionHandler handler)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _externalEvent = externalEvent;
            _handler = handler;

            Loaded += AuditChangesWindow_Loaded;
        }

        private void AuditChangesWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadAndRefreshChanges();
        }

        private void LoadAndRefreshChanges()
        {
            try
            {
                RevitActionExecutor.ScanDocumentForChanges(_uidoc.Document);
                var list = RevitActionExecutor.ChangeHistory.ToList();
                ListChanges.ItemsSource = null;
                ListChanges.ItemsSource = list;
                if (list.Count > 0 && ListChanges.SelectedIndex < 0)
                {
                    ListChanges.SelectedIndex = 0;
                }
            }
            catch { }
        }

        private void ListChanges_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            NavigateToSelectedChange();
        }

        private void BtnInspectSelected_Click(object sender, RoutedEventArgs e)
        {
            NavigateToSelectedChange();
        }

        private void NavigateToSelectedChange()
        {
            var selected = ListChanges.SelectedItem as ModelChangeRecord;
            if (selected == null || selected.ElementId == ElementId.InvalidElementId)
            {
                if (ListChanges.Items.Count > 0)
                {
                    ListChanges.SelectedIndex = 0;
                    selected = ListChanges.SelectedItem as ModelChangeRecord;
                }
            }

            if (selected == null || selected.ElementId == ElementId.InvalidElementId)
            {
                TaskDialog.Show("BIM AIA", "Please select an element from the list first.");
                return;
            }

            _handler.TransactionTitle = $"BIM AIA: Inspect {selected.ChangeType}";
            _handler.ActionToExecute = (uiapp) =>
            {
                RevitActionExecutor.NavigateToVisualAuditView(_uidoc, selected.ElementId);
            };

            _externalEvent.Raise();
        }

        private void BtnScanModel_Click(object sender, RoutedEventArgs e)
        {
            LoadAndRefreshChanges();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

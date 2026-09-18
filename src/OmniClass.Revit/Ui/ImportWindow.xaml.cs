using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace OmniClass.Revit.Ui
{
    public partial class ImportWindow : Window
    {
        private readonly IReadOnlyList<ImportRow> _rows;

        public ImportWindow(IReadOnlyList<ImportRow> rows)
        {
            _rows = rows ?? throw new ArgumentNullException(nameof(rows));
            InitializeComponent();
            foreach (var row in _rows)
                row.PropertyChanged += (_, __) => UpdateSummary();
            Grid.ItemsSource = _rows;
            UpdateSummary();
        }

        public bool ApplyConfirmed { get; private set; }

        public IEnumerable<ImportRow> CheckedRows => _rows.Where(r => r.Apply && r.CanApply);

        private void CheckWritable_OnClick(object sender, RoutedEventArgs e)
        {
            foreach (var row in _rows) row.Apply = row.CanApply;
            UpdateSummary();
        }

        private void UncheckAll_OnClick(object sender, RoutedEventArgs e)
        {
            foreach (var row in _rows) row.Apply = false;
            UpdateSummary();
        }

        private void Apply_OnClick(object sender, RoutedEventArgs e)
        {
            ApplyConfirmed = true;
            DialogResult = true;
            Close();
        }

        private void Cancel_OnClick(object sender, RoutedEventArgs e)
        {
            ApplyConfirmed = false;
            DialogResult = false;
            Close();
        }

        private void UpdateSummary()
        {
            var matched = _rows.Count(r => r.Action.Target != null);
            var classify = _rows.Count(r => r.Action.UpdateClassification);
            var names = _rows.Count(r => r.Action.UpdateName);
            var checkedCount = _rows.Count(r => r.Apply && r.CanApply);

            SummaryText.Text =
                _rows.Count + " CSV row" + (_rows.Count == 1 ? "" : "s") + ". " +
                matched + " matched to a room, " +
                names + " name update" + (names == 1 ? "" : "s") + ", " +
                classify + " classification update" + (classify == 1 ? "" : "s") + ". " +
                checkedCount + " checked to apply.";
        }
    }
}

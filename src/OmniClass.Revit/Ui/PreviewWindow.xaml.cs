using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OmniClass.Core.Matching;

namespace OmniClass.Revit.Ui
{
    public partial class PreviewWindow : Window
    {
        private readonly IReadOnlyList<PreviewRow> _rows;

        public PreviewWindow(IReadOnlyList<PreviewRow> rows)
        {
            _rows = rows ?? throw new ArgumentNullException(nameof(rows));
            InitializeComponent();
            foreach (var row in _rows)
                row.PropertyChanged += (_, __) => UpdateSummary();
            Grid.ItemsSource = _rows;
            UpdateSummary();
        }

        public bool ApplyConfirmed { get; private set; }

        public IEnumerable<PreviewRow> CheckedRows => _rows.Where(r => r.Apply && r.CanApply);

        private void FilterBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Grid == null) return;

            var index = FilterBox.SelectedIndex;
            Grid.ItemsSource = _rows.Where(row =>
            {
                switch (index)
                {
                    case 1: return row.Apply;
                    case 2: return row.Result.NeedsReview;
                    case 3: return row.Result.Status == MatchStatus.Unmatched;
                    case 4: return !row.Room.IsWritable || (row.Room.AlreadyClassified && !row.Apply);
                    default: return true;
                }
            }).ToList();
        }

        private void CheckExact_OnClick(object sender, RoutedEventArgs e)
        {
            foreach (var row in _rows)
                row.Apply = row.CanApply && row.Result.CanAutoApply;
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
            var exact = _rows.Count(r => r.Result.Status == MatchStatus.Exact);
            var review = _rows.Count(r => r.Result.NeedsReview);
            var unmatched = _rows.Count(r => r.Result.Status == MatchStatus.Unmatched);
            var checkedCount = _rows.Count(r => r.Apply && r.CanApply);

            SummaryText.Text =
                _rows.Count + " rooms. " +
                exact + " exact, " +
                review + " need review, " +
                unmatched + " unmatched. " +
                checkedCount + " checked to apply.";
        }
    }
}

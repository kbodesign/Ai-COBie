using System.ComponentModel;
using System.Runtime.CompilerServices;
using OmniClass.Core.Transfer;

namespace OmniClass.Revit.Ui
{
    public sealed class ImportRow : INotifyPropertyChanged
    {
        private bool _apply;

        public ImportRow(RoomImportAction action)
        {
            Action = action ?? throw new System.ArgumentNullException(nameof(action));
            _apply = action.DefaultApply;
        }

        public RoomImportAction Action { get; }

        public bool Apply
        {
            get => _apply;
            set
            {
                if (_apply == value) return;
                _apply = value;
                OnPropertyChanged();
            }
        }

        public bool CanApply => Action.CanApply;
        public string RoomNumber => Action.Target?.Number ?? string.Empty;
        public string CurrentName => Action.Target?.Name ?? string.Empty;
        public string ImportName => Action.Source?.Name ?? string.Empty;
        public string OmniClassNumber => Action.Source?.OmniClassNumber ?? string.Empty;
        public string OmniClassName => Action.Source?.OmniClassName ?? string.Empty;
        public string Match => Action.Match;
        public string Note => Action.Note;
        public string RoomId => Action.Target?.Id ?? string.Empty;

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

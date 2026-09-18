using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using OmniClass.Core.Matching;
using OmniClass.Revit.Rooms;
using OmniClass.Core.Configuration;

namespace OmniClass.Revit.Ui
{
    public sealed class PreviewRow : INotifyPropertyChanged
    {
        private bool _apply;

        public PreviewRow(RoomSnapshot room, ClassificationResult result, AddinSettings settings)
        {
            Room = room;
            Result = result;
            Reason = ApplyPolicy.Reason(
                room.IsUnplaced,
                room.IsNotEnclosed,
                room.OwnedByOtherUser,
                room.OwnerName,
                room.AlreadyClassified,
                settings.OverwriteExisting,
                result.Status);

            _apply = ApplyPolicy.DefaultChecked(
                result.CanAutoApply,
                room.AlreadyClassified,
                settings.OverwriteExisting,
                room.IsWritable);
        }

        public RoomSnapshot Room { get; }
        public ClassificationResult Result { get; }
        public string Reason { get; }

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

        public bool CanApply => Room.IsWritable && !string.IsNullOrEmpty(Result.Number);

        public string RoomNumber => Room.Number;
        public string RoomName => Room.Name;
        public string LevelName => Room.LevelName;
        public string Status => Result.Status.ToString();
        public string ProposedNumber => Result.Number;
        public string ProposedTitle => Result.Title;
        public string MatchedAlias => Result.MatchedAlias;
        public string Score => Result.Score == 0d
            ? string.Empty
            : Result.Score.ToString("0.00", CultureInfo.InvariantCulture);
        public string CurrentNumber => Room.CurrentNumber;
        public string CurrentTitle => Room.CurrentTitle;
        public string OtherCandidates => string.Join(" | ", Result.Candidates.Skip(1));

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using OmniClass.Core.Configuration;

namespace OmniClass.Revit.Rooms
{
    internal static class RoomCollector
    {
        public static List<RoomSnapshot> Collect(Document document, AddinSettings settings)
        {
            var rooms = new List<RoomSnapshot>();

            var collector = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType();

            foreach (var element in collector)
            {
                var room = element as Room;
                if (room == null) continue;
                rooms.Add(Snapshot(document, room, settings));
            }

            rooms.Sort((a, b) =>
            {
                var byNumber = string.CompareOrdinal(a.Number, b.Number);
                return byNumber != 0 ? byNumber : string.CompareOrdinal(a.Name, b.Name);
            });

            return rooms;
        }

        private static RoomSnapshot Snapshot(Document document, Room room, AddinSettings settings)
        {
            var unplaced = room.Location == null;
            var area = room.Area;
            var owned = false;
            var owner = string.Empty;

            if (document.IsWorkshared)
            {
                var status = WorksharingUtils.GetCheckoutStatus(document, room.Id);
                if (status == CheckoutStatus.OwnedByOtherUser)
                {
                    owned = true;
                    owner = WorksharingUtils.GetWorksharingTooltipInfo(document, room.Id).Owner ?? string.Empty;
                }
            }

            return new RoomSnapshot
            {
                Id = ElementIdValue(room.Id),
                Number = room.Number ?? string.Empty,
                Name = RoomName(room),
                LevelName = unplaced || room.Level == null ? string.Empty : room.Level.Name,
                Area = area,
                IsUnplaced = unplaced,
                IsNotEnclosed = !unplaced && area <= 0.0001,
                OwnedByOtherUser = owned,
                OwnerName = owner,
                CurrentNumber = ParameterText(room, settings.NumberParameterName),
                CurrentTitle = ParameterText(room, settings.TitleParameterName),
                CurrentCategory = ParameterText(room, settings.CategoryParameterName),
                HasCobieParameters = ClassificationWriter.HasCobieParameters(room, settings)
            };
        }

        private static string RoomName(Room room)
        {
            var parameter = room.get_Parameter(BuiltInParameter.ROOM_NAME);
            var value = parameter?.AsString();
            return string.IsNullOrEmpty(value) ? (room.Name ?? string.Empty) : value;
        }

        private static string ParameterText(Element element, string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var parameter = element.LookupParameter(name);
            return parameter?.AsString() ?? string.Empty;
        }

        internal static long ElementIdValue(ElementId id)
        {
#if NET8_0_OR_GREATER
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }

        internal static ElementId ToElementId(long id)
        {
#if NET8_0_OR_GREATER
            return new ElementId(id);
#else
            return new ElementId((int)id);
#endif
        }
    }
}

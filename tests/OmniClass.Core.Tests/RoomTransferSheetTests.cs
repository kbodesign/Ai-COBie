using System;
using System.IO;
using System.Linq;
using OmniClass.Core.Transfer;
using Xunit;

namespace OmniClass.Core.Tests
{
    public class RoomTransferSheetTests
    {
        [Fact]
        public void WriteThenLoadKeepsNameAndOmniClassWithoutRoomNumber()
        {
            var rows = new[]
            {
                new RoomTransferRow
                {
                    RoomNumber = "101",
                    Name = "W Room",
                    OmniClassNumber = "13-23 17 13",
                    OmniClassName = "Women's Restroom"
                }
            };

            var writer = new StringWriter();
            RoomTransferSheet.Write(writer, rows);
            var text = writer.ToString();

            Assert.StartsWith("Name,OmniClass Number,OmniClass Name", text);
            Assert.DoesNotContain("Room Number", text);
            Assert.DoesNotContain("101", text);
            Assert.Contains("W Room,13-23 17 13,Women's Restroom", text);

            var loaded = RoomTransferSheet.FromRows(Parse(text));
            var row = Assert.Single(loaded);
            Assert.Equal("", row.RoomNumber);
            Assert.Equal("W Room", row.Name);
            Assert.Equal("13-23 17 13", row.OmniClassNumber);
            Assert.Equal("Women's Restroom", row.OmniClassName);
        }

        [Fact]
        public void LoadsLegacyRoomNumberColumnWithoutUsingItAsTheName()
        {
            var loaded = RoomTransferSheet.FromRows(new[]
            {
                new[] { "Room Number", "Name", "OmniClass Number", "OmniClass Name" },
                new[] { "106", "EMR", "13-55 11 17", "Exam Room" }
            });

            var row = Assert.Single(loaded);
            Assert.Equal("106", row.RoomNumber);
            Assert.Equal("EMR", row.Name);
            Assert.Equal("13-55 11 17", row.OmniClassNumber);
            Assert.Equal("Exam Room", row.OmniClassName);
        }

        [Fact]
        public void AcceptsClassificationSpaceHeaderAliases()
        {
            var loaded = RoomTransferSheet.FromRows(new[]
            {
                new[] { "Number", "Name", "Classification.Space.Number", "Classification.Space.Description" },
                new[] { "102", "MR", "13-23 17 11", "Men's Restroom" }
            });

            var row = Assert.Single(loaded);
            Assert.Equal("102", row.RoomNumber);
            Assert.Equal("MR", row.Name);
            Assert.Equal("13-23 17 11", row.OmniClassNumber);
            Assert.Equal("Men's Restroom", row.OmniClassName);
        }

        [Fact]
        public void AliasDictionaryHeaderIsDetected()
        {
            Assert.True(RoomTransferSheet.LooksLikeAliasDictionary(new[]
            {
                new[] { "Number", "Name", "Room Name 1", "Room Name 2" }
            }));
            Assert.False(RoomTransferSheet.LooksLikeAliasDictionary(new[]
            {
                RoomTransferSheet.Header
            }));
        }

        [Fact]
        public void UniqueKeepsOneRowPerNormalizedName()
        {
            var unique = RoomTransferSheet.Unique(new[]
            {
                new RoomTransferRow { RoomNumber = "101", Name = "W Room" },
                new RoomTransferRow { RoomNumber = "102", Name = "W Room" },
                new RoomTransferRow { RoomNumber = "103", Name = "W_Room", OmniClassNumber = "13-23 17 13", OmniClassName = "Women's Restroom" }
            });

            var row = Assert.Single(unique);
            Assert.Equal("W Room", row.Name);
            Assert.Equal("13-23 17 13", row.OmniClassNumber);
            Assert.Equal("Women's Restroom", row.OmniClassName);
        }

        [Fact]
        public void MergeUniqueAppendsOnlyNewNamesAndFillsBlankOmniClass()
        {
            var existing = new[]
            {
                new RoomTransferRow { RoomNumber = "101", Name = "W Room" },
                new RoomTransferRow { RoomNumber = "201", Name = "Office", OmniClassNumber = "13-55 11", OmniClassName = "Office Spaces" }
            };

            var incoming = new[]
            {
                new RoomTransferRow { RoomNumber = "12", Name = "W Room", OmniClassNumber = "13-23 17 13", OmniClassName = "Women's Restroom" },
                new RoomTransferRow { RoomNumber = "13", Name = "Office" },
                new RoomTransferRow { RoomNumber = "14", Name = "Break Room", OmniClassNumber = "13-57 17 13", OmniClassName = "Break Room" }
            };

            var merged = RoomTransferSheet.MergeUnique(existing, incoming);

            Assert.Equal(1, merged.Added);
            Assert.Equal(1, merged.FilledClassification);
            Assert.Equal(1, merged.AlreadyPresent);
            Assert.Equal(3, merged.Rows.Count);

            var wRoom = Assert.Single(merged.Rows, r => r.Name == "W Room");
            Assert.Equal("13-23 17 13", wRoom.OmniClassNumber);
            Assert.Contains(merged.Rows, r => r.Name == "Break Room");
        }

        [Fact]
        public void SecondMergeOfTheSameNamesAddsNothing()
        {
            var first = new[]
            {
                new RoomTransferRow { RoomNumber = "101", Name = "Corridor", OmniClassNumber = "13-25 11 11", OmniClassName = "Corridor" }
            };

            var sheet = RoomTransferSheet.MergeUnique(Array.Empty<RoomTransferRow>(), first);
            var again = RoomTransferSheet.MergeUnique(sheet.Rows, first);

            Assert.Equal(0, again.Added);
            Assert.Equal(1, again.AlreadyPresent);
            Assert.Single(again.Rows);
        }

        private static System.Collections.Generic.IReadOnlyList<string[]> Parse(string text)
        {
            using (var reader = new StringReader(text))
            {
                return OmniClass.Core.Io.DelimitedText.Parse(reader);
            }
        }
    }

    public class RoomImportPlannerTests
    {
        [Fact]
        public void DoesNotRenameWhenOnlyMarkMatches()
        {
            var actions = RoomImportPlanner.Plan(
                new[]
                {
                    new RoomTransferRow
                    {
                        RoomNumber = "106",
                        Name = "EMR",
                        OmniClassNumber = "13-55 11 17",
                        OmniClassName = "Exam Room"
                    }
                },
                new[]
                {
                    new ProjectRoomRef
                    {
                        Id = "1",
                        Number = "106",
                        Name = "Waiting",
                        IsWritable = true,
                        HasCobieParameters = true
                    }
                });

            var action = Assert.Single(actions);
            Assert.Equal("None", action.Match);
            Assert.Null(action.Target);
            Assert.False(action.UpdateName);
            Assert.False(action.UpdateClassification);
            Assert.False(action.CanApply);
        }

        [Fact]
        public void MatchesEveryRoomWithTheSameNameRegardlessOfMark()
        {
            var actions = RoomImportPlanner.Plan(
                new[]
                {
                    new RoomTransferRow
                    {
                        Name = "EMR",
                        OmniClassNumber = "13-55 11 17",
                        OmniClassName = "Exam Room"
                    }
                },
                new[]
                {
                    Room("1", "106", "EMR"),
                    Room("2", "107", "EMR"),
                    Room("3", "108", "Waiting")
                });

            Assert.Equal(2, actions.Count);
            Assert.All(actions, a =>
            {
                Assert.Equal("Name", a.Match);
                Assert.Equal("EMR", a.Target.Name);
                Assert.False(a.UpdateName);
                Assert.True(a.UpdateClassification);
            });
            Assert.Equal(new[] { "106", "107" }, actions.Select(a => a.Target.Number).ToArray());
        }

        [Fact]
        public void MatchesNormalizedNameAndPlansSpellingUpdate()
        {
            var actions = RoomImportPlanner.Plan(
                new[]
                {
                    new RoomTransferRow
                    {
                        Name = "W Room",
                        OmniClassNumber = "13-23 17 13",
                        OmniClassName = "Women's Restroom"
                    }
                },
                new[]
                {
                    Room("1", "205", "W_Room")
                });

            var action = Assert.Single(actions);
            Assert.Equal("Name", action.Match);
            Assert.True(action.UpdateName);
            Assert.Equal("W Room", action.Source.Name);
            Assert.True(action.UpdateClassification);
        }

        [Fact]
        public void MatchesByNameAndPlansClassificationWithoutRenaming()
        {
            var actions = RoomImportPlanner.Plan(
                new[]
                {
                    new RoomTransferRow
                    {
                        RoomNumber = "999",
                        Name = "W Room",
                        OmniClassNumber = "13-23 17 13",
                        OmniClassName = "Women's Restroom"
                    }
                },
                new[]
                {
                    Room("1", "205", "W Room")
                });

            var action = Assert.Single(actions);
            Assert.Equal("Name", action.Match);
            Assert.False(action.UpdateName);
            Assert.True(action.UpdateClassification);
        }

        [Fact]
        public void DoesNotPlanClassificationWhenCobieIsNotOnTheRoom()
        {
            var actions = RoomImportPlanner.Plan(
                new[]
                {
                    new RoomTransferRow
                    {
                        Name = "W Room",
                        OmniClassNumber = "13-23 17 13",
                        OmniClassName = "Women's Restroom"
                    }
                },
                new[]
                {
                    new ProjectRoomRef
                    {
                        Id = "1",
                        Number = "101",
                        Name = "W Room",
                        IsWritable = true,
                        HasCobieParameters = false
                    }
                });

            var action = Assert.Single(actions);
            Assert.Equal("Name", action.Match);
            Assert.False(action.UpdateClassification);
            Assert.False(action.CanApply);
            Assert.Contains("COBie", action.Note);
        }

        [Fact]
        public void UnmatchedRowsStayOnTheSheetUnchecked()
        {
            var actions = RoomImportPlanner.Plan(
                new[]
                {
                    new RoomTransferRow { RoomNumber = "999", Name = "Storage" }
                },
                new[]
                {
                    new ProjectRoomRef { Id = "1", Number = "101", Name = "Office", IsWritable = true, HasCobieParameters = true }
                });

            var action = Assert.Single(actions);
            Assert.Equal("None", action.Match);
            Assert.Null(action.Target);
            Assert.False(action.CanApply);
            Assert.False(action.DefaultApply);
        }

        private static ProjectRoomRef Room(string id, string number, string name)
        {
            return new ProjectRoomRef
            {
                Id = id,
                Number = number,
                Name = name,
                IsWritable = true,
                HasCobieParameters = true
            };
        }
    }
}

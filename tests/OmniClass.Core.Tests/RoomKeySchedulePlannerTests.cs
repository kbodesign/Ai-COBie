using System;
using OmniClass.Core.Transfer;
using Xunit;

namespace OmniClass.Core.Tests
{
    public class RoomKeySchedulePlannerTests
    {
        [Fact]
        public void AddsEveryUniqueNameWhenTheScheduleIsEmpty()
        {
            var plan = RoomKeySchedulePlanner.Plan(
                new[]
                {
                    new RoomTransferRow { Name = "EMR", OmniClassNumber = "13-55 11 17", OmniClassName = "Exam Room" },
                    new RoomTransferRow { Name = "Waiting" },
                    new RoomTransferRow { Name = "EMR" }
                },
                Array.Empty<RoomKeyRef>());

            Assert.Equal(2, plan.AddCount);
            Assert.Equal(2, plan.NewRowCount);
            Assert.Equal(0, plan.UpdateCount);
            Assert.Contains(plan.Actions, a => a.Source.Name == "EMR" && a.UpdateClassification);
            Assert.Contains(plan.Actions, a => a.Source.Name == "Waiting" && !a.UpdateClassification);
        }

        [Fact]
        public void DoesNotRenameADifferentKeyWhenOnlyAMarkWouldHaveMatched()
        {
            var plan = RoomKeySchedulePlanner.Plan(
                new[]
                {
                    new RoomTransferRow { RoomNumber = "106", Name = "EMR" }
                },
                new[]
                {
                    new RoomKeyRef { Id = "1", KeyName = "Waiting", Name = "Waiting" }
                });

            var action = Assert.Single(plan.Actions);
            Assert.Equal("Add", action.Kind);
            Assert.True(action.NeedsNewRow);
        }

        [Fact]
        public void UpdatesOmniClassOnAnExistingKeyWithTheSameName()
        {
            var plan = RoomKeySchedulePlanner.Plan(
                new[]
                {
                    new RoomTransferRow { Name = "EMR", OmniClassNumber = "13-55 11 17", OmniClassName = "Exam Room" }
                },
                new[]
                {
                    new RoomKeyRef { Id = "1", KeyName = "EMR", Name = "EMR" }
                });

            var action = Assert.Single(plan.Actions);
            Assert.Equal("Update", action.Kind);
            Assert.False(action.UpdateKeyName);
            Assert.False(action.UpdateName);
            Assert.True(action.UpdateClassification);
            Assert.False(action.NeedsNewRow);
        }

        [Fact]
        public void MatchesNormalizedKeyNameAndPlansSpellingUpdate()
        {
            var plan = RoomKeySchedulePlanner.Plan(
                new[] { new RoomTransferRow { Name = "W Room" } },
                new[] { new RoomKeyRef { Id = "1", KeyName = "W_Room", Name = "W_Room" } });

            var action = Assert.Single(plan.Actions);
            Assert.Equal("Update", action.Kind);
            Assert.True(action.UpdateKeyName);
            Assert.True(action.UpdateName);
        }

        [Fact]
        public void ReusesABlankKeyInsteadOfInsertingARow()
        {
            var plan = RoomKeySchedulePlanner.Plan(
                new[] { new RoomTransferRow { Name = "EMR" } },
                new[] { new RoomKeyRef { Id = "9", KeyName = "", Name = "" } });

            var action = Assert.Single(plan.Actions);
            Assert.Equal("Add", action.Kind);
            Assert.Equal("9", action.Target.Id);
            Assert.False(action.NeedsNewRow);
        }

        [Fact]
        public void LeavesKeysThatAreNotInTheCsvAlone()
        {
            var plan = RoomKeySchedulePlanner.Plan(
                new[] { new RoomTransferRow { Name = "EMR" } },
                new[]
                {
                    new RoomKeyRef { Id = "1", KeyName = "EMR", Name = "EMR" },
                    new RoomKeyRef { Id = "2", KeyName = "Waiting", Name = "Waiting" }
                });

            var action = Assert.Single(plan.Actions);
            Assert.Equal("Skip", action.Kind);
            Assert.Equal(1, plan.SkipCount);
        }
    }
}

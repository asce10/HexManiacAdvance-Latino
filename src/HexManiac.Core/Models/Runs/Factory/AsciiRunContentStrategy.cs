﻿using System;
using System.Collections.Generic;

namespace HavenSoft.HexManiac.Core.Models.Runs.Factory {
   /// <summary>
   /// Format Specifier:     `asc`
   /// ASCII runs are not currently supported within tables.
   /// </summary>
   public class AsciiRunContentStrategy : RunStrategy {
      public override int LengthForNewRun(IDataModel model, int pointerAddress) {
         if (!int.TryParse(Format.Substring(5), out var length)) length = 1;
         return length.LimitToRange(1, 1000);
      }

      public override bool Matches(IFormattedRun run) => run is AsciiRun;

      public override bool TryAddFormatAtDestination(IDataModel owner, ModelDelta token, int source, int destination, string name, IReadOnlyList<ArrayRunElementSegment> sourceSegments, int parentIndex) {
         if (!int.TryParse(Format.Substring(5), out var length)) length = 0;
         if (length < 1) return false;
         length = length.LimitToRange(1, 1000);
         if (token is not NoDataChangeDeltaModel) owner.ObserveRunWritten(token, new AsciiRun(owner, destination, length, SortedSpan.One(source)));
         return true;
      }

      public override void UpdateNewRunFromPointerFormat(IDataModel model, ModelDelta token, string name, IReadOnlyList<ArrayRunElementSegment> sourceSegments, int parentIndex, ref IFormattedRun run) {
         if (!int.TryParse(Format.Substring(5), out var length)) length = 1;
         length = length.LimitToRange(1, 1000);
         run = new AsciiRun(model, run.Start, length, run.PointerSources);
         model.ClearFormat(token, run.Start, run.Length);
      }

      public override IFormattedRun WriteNewRun(IDataModel owner, ModelDelta token, int source, int destination, string name, IReadOnlyList<ArrayRunElementSegment> sourceSegments) {
         if (!int.TryParse(Format.Substring(5), out var length)) length = 1;
         return new AsciiRun(owner, destination, length);
      }

      public override ErrorInfo TryParseData(IDataModel model, string name, int dataIndex, ref IFormattedRun run) {
         if (int.TryParse(Format.Substring(5), out var length)) {
            run = new AsciiRun(model, dataIndex, length);
         } else {
            return new ErrorInfo($"Ascii runs must include a length.");
         }

         return ErrorInfo.NoError;
      }
   }

   public class BrailleRunContentStrategy : RunStrategy {
      public override int LengthForNewRun(IDataModel model, int pointerAddress) => throw new NotImplementedException();

      public override bool Matches(IFormattedRun run) => throw new NotImplementedException();

      public override bool TryAddFormatAtDestination(IDataModel owner, ModelDelta token, int source, int destination, string name, IReadOnlyList<ArrayRunElementSegment> sourceSegments, int parentIndex) => throw new NotImplementedException();

      public override ErrorInfo TryParseData(IDataModel model, string name, int dataIndex, ref IFormattedRun run) {
         run = new BrailleRun(model, dataIndex);
         return ErrorInfo.NoError;
      }

      public override void UpdateNewRunFromPointerFormat(IDataModel model, ModelDelta token, string name, IReadOnlyList<ArrayRunElementSegment> sourceSegments, int parentIndex, ref IFormattedRun run) => throw new NotImplementedException();

      public override IFormattedRun WriteNewRun(IDataModel owner, ModelDelta token, int source, int destination, string name, IReadOnlyList<ArrayRunElementSegment> sourceSegments) => throw new NotImplementedException();
   }
}

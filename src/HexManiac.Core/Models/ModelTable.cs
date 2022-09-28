﻿using HavenSoft.HexManiac.Core.Models.Runs;
using HavenSoft.HexManiac.Core.Models.Runs.Sprites;
using HavenSoft.HexManiac.Core.ViewModels.DataFormats;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;

namespace HavenSoft.HexManiac.Core.Models {
   public class ModelTable : IReadOnlyList<ModelArrayElement> {
      private readonly IDataModel model;
      private readonly int arrayAddress;
      private readonly Func<ModelDelta> tokenFactory;
      private readonly ITableRun run;

      public int Count => run?.ElementCount ?? 0;
      public int __len__() => Count; // for python

      public ITableRun Run => run;

      public ModelArrayElement this[int value] {
         get {
            return new ModelArrayElement(model, arrayAddress, value, tokenFactory, run);
         }
      }

      public ModelArrayElement this[string value] {
         get {
            var table = run;
            if (ArrayRunEnumSegment.TryMatch(value, table.ElementNames, out int index)) {
               return this[index];
            } else {
               throw new NotImplementedException();
            }
         }
      }

      public ModelTable(IDataModel model, ITableRun table, Func<ModelDelta> tokenFactory = null) : this(model, table.Start, tokenFactory, table) { }

      public ModelTable(IDataModel model, int address, Func<ModelDelta> tokenFactory = null, ITableRun tableRun = null) {
         (this.model, arrayAddress) = (model, address);
         run = tableRun ?? model.GetNextRun(address) as ITableRun;
         this.tokenFactory = tokenFactory ?? (() => new NoDataChangeDeltaModel());
      }

      public IEnumerator<ModelArrayElement> GetEnumerator() {
         var count = Count;
         for (int i = 0; i < count; i++) yield return this[i];
      }

      IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
   }

   public class ModelArrayElement : DynamicObject {
      private readonly IDataModel model;
      private readonly int arrayAddress;
      private readonly int arrayIndex;
      private readonly ITableRun table;
      private readonly Func<ModelDelta> tokenFactory;

      public int Start => table.Start + table.ElementLength * arrayIndex;
      public int Length => table.ElementLength;
      public string Address => (table.Start + table.ElementLength * arrayIndex).ToAddress();
      public ITableRun Table => table;
      public IDataModel Model => model;
      public ModelDelta Token => tokenFactory();

      public ModelArrayElement(IDataModel model, int address, int index, Func<ModelDelta> tokenFactory, ITableRun table) {
         (this.model, arrayAddress, arrayIndex) = (model, address, index);
         this.table = table ?? (ITableRun)model.GetNextRun(arrayAddress);
         this.tokenFactory = tokenFactory;
      }

      public bool HasField(string name) => table.ElementContent.Any(field => field.Name == name);

      public string GetFieldName(int index) => table.ElementContent[index].Name;

      public string GetStringValue(string fieldName) {
         var elementOffset = table.ElementContent.Until(segment => segment.Name == fieldName).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
         if (seg.Type == ElementContentType.PCS) {
            return model.TextConverter.Convert(model, valueAddress, seg.Length).Trim('"');
         } else if (seg.Type == ElementContentType.Pointer) {
            valueAddress = model.ReadPointer(valueAddress);
            if (valueAddress < 0 || valueAddress >= model.Count) return string.Empty;
            var length = PCSString.ReadString(model, valueAddress, true);
            return model.TextConverter.Convert(model, valueAddress, length).Trim('"');
         } else {
            throw new NotImplementedException();
         }
      }

      public string Serialize(string fieldName) {
         var elementOffset = table.ElementContent.Until(segment => segment.Name == fieldName).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
         if (seg.Type == ElementContentType.Pointer) {
            var destination = model.ReadPointer(valueAddress);
            if (destination == Pointer.NULL) return "null";
            var run = model.GetNextRun(destination);
            if (run is ArrayRun tRun) {
               run = new TableStreamRun(model, run.Start, run.PointerSources, run.FormatString,
                        tRun.ElementContent, new FixedLengthStreamStrategy(tRun.ElementCount));
            }
            if (run is IStreamRun sRun) return sRun.SerializeRun();
            return destination.ToAddress();
         }
         if (seg.Type == ElementContentType.PCS) return GetStringValue(fieldName);
         if (seg is ArrayRunEnumSegment) return GetEnumValue(fieldName);
         return GetValue(fieldName).ToString();
      }

      public int[,] GetSprite(string fieldName) {
         var elementOffset = table.ElementContent.Until(segment => segment.Name == fieldName).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
         if (seg is ArrayRunPointerSegment pointerSeg) {
            var destination = model.ReadPointer(valueAddress);
            if (model.GetNextRun(destination) is not ISpriteRun spriteRun) {
               IFormattedRun tempRun = new NoInfoRun(destination, new SortedSpan<int>(valueAddress));
               model.FormatRunFactory.GetStrategy(pointerSeg.InnerFormat).TryParseData(model, string.Empty, destination, ref tempRun);
               spriteRun = tempRun as ISpriteRun;
               if (spriteRun == null) return null;
            }
            return spriteRun.GetPixels(model, 0, arrayIndex);
         } else {
            throw new NotImplementedException();
         }
      }

      public IReadOnlyList<short> GetPalette(string fieldName) {
         var elementOffset = table.ElementContent.Until(segment => segment.Name == fieldName).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
         if (seg is ArrayRunPointerSegment pointerSeg) {
            var destination = model.ReadPointer(valueAddress);
            if (model.GetNextRun(destination) is not IPaletteRun palRun) {
               IFormattedRun tempRun = new NoInfoRun(destination, new SortedSpan<int>(valueAddress));
               model.FormatRunFactory.GetStrategy(pointerSeg.InnerFormat).TryParseData(model, string.Empty, destination, ref tempRun);
               palRun = tempRun as IPaletteRun;
               if (palRun == null) return null;
            }
            return palRun.GetPalette(model, 0);
         } else {
            throw new NotImplementedException();
         }
      }

      public int GetValue(string fieldName) {
         var elementOffset = table.ElementContent.Until(segment => segment.Name == fieldName).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
         if (seg.Type == ElementContentType.Integer) {
            return model.ReadMultiByteValue(valueAddress, seg.Length);
         } else {
            throw new NotImplementedException();
         }
      }

      public int GetValue(int fieldIndex) {
         var elementOffset = table.ElementContent.Take(fieldIndex).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent[fieldIndex];
         if (seg.Type == ElementContentType.Integer) {
            return model.ReadMultiByteValue(valueAddress, seg.Length);
         } else {
            throw new NotImplementedException();
         }
      }

      public int GetAddress(string fieldName) {
         var elementOffset = table.ElementContent.Until(segment => segment.Name == fieldName).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
         if (seg.Type == ElementContentType.Pointer) {
            return model.ReadPointer(valueAddress);
         } else if (seg.Type == ElementContentType.Integer && seg.Length == 4) {
            return model.ReadPointer(valueAddress); // user wants a pointer, read it like a pointer
         } else {
            throw new NotImplementedException();
         }
      }

      public string GetEnumValue(string fieldName) {
         var elementOffset = table.ElementContent.Until(segment => segment.Name == fieldName).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
         if (seg is ArrayRunEnumSegment enumSeg) {
            using (ModelCacheScope.CreateScope(model)) {
               return enumSeg.ToText(model, valueAddress, false);
            }
         } else {
            throw new NotImplementedException();
         }
      }

      public object this[string fieldName] {
         get {
            var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
            if (seg is ArrayRunEnumSegment) return GetEnumValue(fieldName);
            if (seg.Type == ElementContentType.Pointer) {
               var address = GetAddress(fieldName);
               if (model.GetNextRun(address) is ITableRun table1) {
                  return new ModelTable(model, table1.Start, tokenFactory);
               }
               return address;
            }
            if (seg.Type == ElementContentType.PCS) return GetStringValue(fieldName);
            return GetValue(fieldName);
         }
         set {
            var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
            if (seg is ArrayRunEnumSegment) SetEnumValue(fieldName, (string)value);
            else if (seg.Type == ElementContentType.Pointer) SetAddress(fieldName, (int)value);
            else if (seg.Type == ElementContentType.PCS) SetStringValue(fieldName, (string)value);
            else SetValue(fieldName, (int)value);
         }
      }

      public void SetEnumValue(string fieldName, string value) {
         var elementOffset = table.ElementContent.Until(segment => segment.Name == fieldName).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
         if (seg is ArrayRunEnumSegment enumSeg) {
            enumSeg.Write(null, model, tokenFactory(), valueAddress, ref value);
         } else {
            throw new NotImplementedException();
         }
      }

      public void SetAddress(string fieldName, int value) {
         var elementOffset = table.ElementContent.Until(segment => segment.Name == fieldName).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
         if (seg.Type == ElementContentType.Pointer) {
            model.UpdateArrayPointer(Token, seg, table.ElementContent, arrayIndex, valueAddress, value);
         } else {
            throw new NotImplementedException();
         }
      }

      public void SetStringValue(string fieldName, string value) {
         var elementOffset = table.ElementContent.Until(segment => segment.Name == fieldName).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
         var token = tokenFactory();
         if (seg.Type == ElementContentType.PCS) {
            var bytes = model.TextConverter.Convert(value, out var _);
            while (bytes.Count > seg.Length) bytes.RemoveAt(bytes.Count - 1);
            bytes[bytes.Count - 1] = 0xFF;
            while (bytes.Count < seg.Length) bytes.Add(0);
            token.ChangeData(model, valueAddress, bytes);
         } else if (seg.Type == ElementContentType.Pointer) {
            valueAddress = model.ReadPointer(valueAddress);
            var length = PCSString.ReadString(model, valueAddress, true);
            var bytes = model.TextConverter.Convert(value, out var _);
            var pcsRun = (PCSRun)model.GetNextRun(valueAddress);
            pcsRun = model.RelocateForExpansion(token, pcsRun, bytes.Count);
            token.ChangeData(model, pcsRun.Start, bytes);
            for (int i = pcsRun.Length; i < length; i++) token.ChangeData(model, pcsRun.Start + i, 0xFF);
         } else {
            throw new NotImplementedException();
         }
      }

      public void SetValue(string fieldName, int value) {
         var elementOffset = table.ElementContent.Until(segment => segment.Name == fieldName).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
         if (seg.Type == ElementContentType.Integer || seg.Type == ElementContentType.BitArray) {
            model.WriteMultiByteValue(valueAddress, seg.Length, tokenFactory(), value);
         } else {
            throw new NotImplementedException();
         }
      }

      public void SetValue(int fieldIndex, int value) {
         var elementOffset = table.ElementContent.Take(fieldIndex).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent[fieldIndex];
         if (seg.Type == ElementContentType.Integer || seg.Type == ElementContentType.BitArray) {
            model.WriteMultiByteValue(valueAddress, seg.Length, tokenFactory(), value);
         } else {
            throw new NotImplementedException();
         }
      }

      public ModelTable GetSubTable(string fieldName) {
         var elementOffset = table.ElementContent.Until(segment => segment.Name == fieldName).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
         if (seg is ArrayRunPointerSegment pointerSeg) {
            var destination = model.ReadPointer(valueAddress);
            if (destination == Pointer.NULL) return null;
            var error = ArrayRun.TryParse(model, fieldName, pointerSeg.InnerFormat, destination, SortedSpan.One(valueAddress), table.ElementContent, out var childTable);
            if (error.HasError) {
               childTable = model.GetNextRun(destination) as ITableRun;
            }
            if (childTable == null) return null;
            return new ModelTable(model, destination, tokenFactory, childTable);
         } else {
            throw new NotImplementedException();
         }
      }

      #region DynamicObject

      public override bool TryGetMember(GetMemberBinder binder, out object? result) {
         result = null;
         var seg = table.ElementContent.FirstOrDefault(segment => segment.Name == binder.Name);
         if (seg == null) return base.TryGetMember(binder, out result);
         result = this[seg.Name];
         return true;
      }

      public override bool TrySetMember(SetMemberBinder binder, object? value) {
         var seg = table.ElementContent.FirstOrDefault(segment => segment.Name == binder.Name);
         if (seg == null) return base.TrySetMember(binder, value);
         this[seg.Name] = value;
         return true;
      }

      #endregion
   }
}

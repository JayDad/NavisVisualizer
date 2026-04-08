using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using NavisVisualizer.Models;

namespace NavisVisualizer.Services
{
    public class UserDataService
    {
        private const string CategoryDisplayName = "User Property";
        private const string CategoryInternalName = "User Property";

        private static object _enumPropVec;
        private static object _enumProp;

        /// <summary>
        /// Test writing a single property and verify it can be read back.
        /// Returns diagnostic message.
        /// </summary>
        public string TestWriteOneProperty(ModelItem item)
        {
            var diag = new StringBuilder();
            var bf = BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance;

            try
            {
                diag.AppendLine($"=== 대상 아이템 ===");
                diag.AppendLine($"DisplayName: {item.DisplayName ?? "(none)"}");
                diag.AppendLine($"ClassName: {item.ClassName}");
                diag.AppendLine($"입력: 카테고리=\"{CategoryDisplayName}\", 속성=\"Test\", 값=\"Hello\"");
                diag.AppendLine();

                dynamic comState = ComApiBridge.State;
                ResolveEnums();
                diag.AppendLine($"Enum OK");

                object comPath = (object)ComApiBridge.ToInwOaPath(item);
                diag.AppendLine($"Path: {comPath.GetType().Name}");

                // GetGUIPropertyNode via IDispatch (InvokeMember)
                object stateObj = (object)comState;
                object propNode = stateObj.GetType().InvokeMember(
                    "GetGUIPropertyNode", bf, null, stateObj,
                    new object[] { comPath, true });
                diag.AppendLine($"PropNode: {propNode.GetType().Name}");

                // Create property vector via IDispatch
                object propVec = stateObj.GetType().InvokeMember(
                    "ObjectFactory", bf, null, stateObj,
                    new object[] { _enumPropVec });
                diag.AppendLine($"PropVec: {propVec.GetType().Name}");

                // Create property
                object prop = stateObj.GetType().InvokeMember(
                    "ObjectFactory", bf, null, stateObj,
                    new object[] { _enumProp });
                diag.AppendLine($"Prop: {prop.GetType().Name}");

                // Set property fields via IDispatch
                var propType = prop.GetType();
                propType.InvokeMember("name", BindingFlags.SetProperty, null, prop, new object[] { "test_name" });
                propType.InvokeMember("UserName", BindingFlags.SetProperty, null, prop, new object[] { "Test Property" });
                propType.InvokeMember("value", BindingFlags.SetProperty, null, prop, new object[] { "Hello from NavisVisualizer" });
                diag.AppendLine("Prop fields set");

                // Add prop to propVec.Properties()
                object propsCollection = propVec.GetType().InvokeMember(
                    "Properties", bf, null, propVec, null);
                propsCollection.GetType().InvokeMember(
                    "Add", bf, null, propsCollection, new object[] { prop });
                diag.AppendLine("Prop added to vec");

                // Read UserDefined count before
                object userDefBefore = null;
                int countBefore = 0;
                try
                {
                    userDefBefore = propNode.GetType().InvokeMember(
                        "UserDefined", bf, null, propNode, null);
                    countBefore = (int)userDefBefore.GetType().InvokeMember(
                        "Count", BindingFlags.GetProperty, null, userDefBefore, null);
                    diag.AppendLine($"UserDefined count before: {countBefore}");
                }
                catch (Exception ex)
                {
                    diag.AppendLine($"UserDefined() failed: {ex.InnerException?.Message ?? ex.Message}");
                    diag.AppendLine("Trying SetUserDefined anyway...");
                }

                // Call SetUserDefined via IDispatch
                propNode.GetType().InvokeMember(
                    "SetUserDefined", bf, null, propNode,
                    new object[] { 0, CategoryInternalName, CategoryDisplayName, propVec });
                diag.AppendLine("SetUserDefined called OK!");

                // Verify
                try
                {
                    object userDefAfter = propNode.GetType().InvokeMember(
                        "UserDefined", bf, null, propNode, null);
                    int countAfter = (int)userDefAfter.GetType().InvokeMember(
                        "Count", BindingFlags.GetProperty, null, userDefAfter, null);
                    diag.AppendLine($"UserDefined count after: {countAfter}");

                    if (countAfter > countBefore)
                        diag.AppendLine("SUCCESS! 속성이 추가되었습니다.");
                    else
                        diag.AppendLine("FAIL: count 변화 없음");
                }
                catch (Exception ex)
                {
                    diag.AppendLine($"Verify failed: {ex.InnerException?.Message ?? ex.Message}");
                }
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException;
                diag.AppendLine($"EXCEPTION: {(inner ?? ex).GetType().Name}: {(inner ?? ex).Message}");
            }

            return diag.ToString();
        }

        public int WriteSpoolProperties(
            List<SpoolData> spools,
            Dictionary<string, List<ModelItem>> searchResult,
            DateTime referenceDate)
        {
            object comState = (object)ComApiBridge.State;
            ResolveEnums();
            var bf = BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance;

            int written = 0;
            int attempted = 0;
            string lastError = null;

            foreach (var spool in spools)
            {
                if (!searchResult.TryGetValue(spool.SpoolId, out var items) || items.Count == 0)
                    continue;

                var stage = spool.GetStageAtDate(referenceDate);

                foreach (var item in items)
                {
                    attempted++;
                    try
                    {
                        object comPath = (object)ComApiBridge.ToInwOaPath(item);
                        object propNode = comState.GetType().InvokeMember(
                            "GetGUIPropertyNode", bf, null, comState, new object[] { comPath, true });
                        object propVec = comState.GetType().InvokeMember(
                            "ObjectFactory", bf, null, comState, new object[] { _enumPropVec });

                        // Spool Number
                        AddProp(comState, propVec, bf, "SpoolNumber", "Spool Number", spool.SpoolId);

                        // ISO No
                        if (!string.IsNullOrEmpty(spool.IsoNo))
                            AddProp(comState, propVec, bf, "IsoNo", "ISO No", spool.IsoNo);

                        // Current Stage
                        string stageLabel = SpoolStageInfo.Labels.TryGetValue(stage, out var lbl)
                            ? lbl : stage.ToString();
                        AddProp(comState, propVec, bf, "CurrentStage", "현재 단계", stageLabel);

                        // All stage dates
                        foreach (var s in SpoolStageInfo.OrderedStages)
                        {
                            string label = SpoolStageInfo.Labels[s];
                            string dateStr = spool.StageDates.TryGetValue(s, out var date) && date.HasValue
                                ? date.Value.ToString("yyyy-MM-dd") : "";
                            AddProp(comState, propVec, bf, s.ToString(), label, dateStr);
                        }

                        // Write
                        propNode.GetType().InvokeMember(
                            "SetUserDefined", bf, null, propNode,
                            new object[] { 0, CategoryInternalName, CategoryDisplayName, propVec });
                        written++;
                    }
                    catch (Exception ex)
                    {
                        var inner = ex.InnerException;
                        lastError = (inner ?? ex).GetType().Name + ": " + (inner ?? ex).Message;
                    }
                }
            }

            if (attempted == 0)
                throw new Exception("속성 삽입 대상이 없습니다.");
            if (written == 0 && lastError != null)
                throw new Exception($"0/{attempted}건 실패: {lastError}");

            return written;
        }

        public int WriteEquipmentProperties(
            List<EquipmentData> equipments,
            Dictionary<string, List<ModelItem>> searchResult,
            DateTime referenceDate)
        {
            object comState = (object)ComApiBridge.State;
            ResolveEnums();
            var bf = BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance;

            int written = 0;
            int attempted = 0;
            string lastError = null;

            foreach (var equip in equipments)
            {
                if (!searchResult.TryGetValue(equip.TagNo, out var items) || items.Count == 0)
                    continue;

                var stage = equip.GetStageAtDate(referenceDate);

                foreach (var item in items)
                {
                    attempted++;
                    try
                    {
                        object propVec = BuildEquipmentPropVec(comState, bf, equip, stage);

                        // Write to the matched item itself
                        WriteToItem(comState, bf, item, propVec);
                        written++;

                        // Also write to parent (for prefix matches like Tag/VENSKID)
                        var parent = item.Parent;
                        if (parent != null)
                        {
                            string parentName = parent.DisplayName?.TrimStart('/').Trim() ?? "";
                            string tagUpper = equip.TagNo.ToUpperInvariant();
                            // Write to parent if its name matches or starts with the tag
                            if (parentName.ToUpperInvariant().StartsWith(tagUpper) ||
                                tagUpper.StartsWith(parentName.ToUpperInvariant()))
                            {
                                try
                                {
                                    object parentPropVec = BuildEquipmentPropVec(comState, bf, equip, stage);
                                    WriteToItem(comState, bf, parent, parentPropVec);
                                }
                                catch { }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        var inner = ex.InnerException;
                        lastError = (inner ?? ex).GetType().Name + ": " + (inner ?? ex).Message;
                    }
                }
            }

            if (attempted == 0)
                throw new Exception("속성 삽입 대상이 없습니다.");
            if (written == 0 && lastError != null)
                throw new Exception($"0/{attempted}건 실패: {lastError}");

            return written;
        }

        private object BuildEquipmentPropVec(object comState, BindingFlags bf, EquipmentData equip, EquipmentStage stage)
        {
            object propVec = comState.GetType().InvokeMember(
                "ObjectFactory", bf, null, comState, new object[] { _enumPropVec });

            AddProp(comState, propVec, bf, "TagNo", "Tag No.", equip.TagNo);
            AddProp(comState, propVec, bf, "Description", "Description", equip.Description ?? "");
            AddProp(comState, propVec, bf, "SubSystem", "Sub System", equip.SubSystem ?? "");
            AddProp(comState, propVec, bf, "RfqNo", "RFQ No.", equip.RfqNo ?? "");
            AddProp(comState, propVec, bf, "DeliveryStatus", "Delivery", equip.DeliveryStatus ?? "");

            string stageLabel = EquipmentStageInfo.Labels.TryGetValue(stage, out var lbl) ? lbl : stage.ToString();
            AddProp(comState, propVec, bf, "CurrentStage", "현재 단계", stageLabel);

            if (equip.ConfirmedEta.HasValue)
                AddProp(comState, propVec, bf, "ETA", "Confirmed ETA", equip.ConfirmedEta.Value.ToString("yyyy-MM-dd"));

            foreach (var s in EquipmentStageInfo.OrderedStages)
            {
                if (s == EquipmentStage.Delivery) continue;
                string label = EquipmentStageInfo.Labels[s];
                string dateStr = equip.StageDates.TryGetValue(s, out var date) && date.HasValue
                    ? date.Value.ToString("yyyy-MM-dd") : "";
                AddProp(comState, propVec, bf, s.ToString(), label, dateStr);
            }

            return propVec;
        }

        private void WriteToItem(object comState, BindingFlags bf, ModelItem item, object propVec)
        {
            object comPath = (object)ComApiBridge.ToInwOaPath(item);
            object propNode = comState.GetType().InvokeMember(
                "GetGUIPropertyNode", bf, null, comState, new object[] { comPath, true });
            propNode.GetType().InvokeMember(
                "SetUserDefined", bf, null, propNode,
                new object[] { 0, CategoryInternalName, CategoryDisplayName, propVec });
        }

        private void AddProp(object comState, object propVec, BindingFlags bf,
            string internalName, string displayName, string value)
        {
            object prop = comState.GetType().InvokeMember(
                "ObjectFactory", bf, null, comState, new object[] { _enumProp });
            var propType = prop.GetType();
            propType.InvokeMember("name", BindingFlags.SetProperty, null, prop, new object[] { internalName });
            propType.InvokeMember("UserName", BindingFlags.SetProperty, null, prop, new object[] { displayName });
            propType.InvokeMember("value", BindingFlags.SetProperty, null, prop, new object[] { value });

            object propsCol = propVec.GetType().InvokeMember("Properties", bf, null, propVec, null);
            propsCol.GetType().InvokeMember("Add", bf, null, propsCol, new object[] { prop });
        }

        private static void ResolveEnums()
        {
            if (_enumPropVec != null) return;

            Type enumType = null;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!asm.GetName().Name.Contains("Interop")) continue;
                try
                {
                    foreach (var t in asm.GetExportedTypes())
                    {
                        if (t.IsEnum && t.Name == "nwEObjectType")
                        { enumType = t; break; }
                    }
                }
                catch { }
                if (enumType != null) break;
            }

            if (enumType == null)
            {
                try
                {
                    string dir = System.IO.Path.GetDirectoryName(
                        typeof(Autodesk.Navisworks.Api.Application).Assembly.Location);
                    string dll = System.IO.Path.Combine(dir, "Autodesk.Navisworks.Interop.ComApi.dll");
                    if (System.IO.File.Exists(dll))
                    {
                        var asm = Assembly.LoadFrom(dll);
                        foreach (var t in asm.GetExportedTypes())
                        {
                            if (t.IsEnum && t.Name == "nwEObjectType")
                            { enumType = t; break; }
                        }
                    }
                }
                catch { }
            }

            if (enumType == null)
                throw new Exception("nwEObjectType enum을 찾을 수 없습니다.");

            _enumPropVec = Enum.Parse(enumType, "eObjectType_nwOaPropertyVec");
            _enumProp = Enum.Parse(enumType, "eObjectType_nwOaProperty");
        }
    }
}

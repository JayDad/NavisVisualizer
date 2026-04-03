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
        private const string CategoryDisplayName = "Spool 실적";
        private const string CategoryInternalName = "LcOaNvSpool";

        private static object _enumPropVec;
        private static object _enumProp;

        /// <summary>
        /// Test writing a single property and verify it can be read back.
        /// Returns diagnostic message.
        /// </summary>
        public string TestWriteOneProperty(ModelItem item)
        {
            var diag = new StringBuilder();
            try
            {
                dynamic comState = ComApiBridge.State;
                ResolveEnums();
                diag.AppendLine($"Enum resolved: PropVec={_enumPropVec}, Prop={_enumProp}");

                dynamic comPath = ComApiBridge.ToInwOaPath(item);
                diag.AppendLine($"Path type: {((object)comPath).GetType().Name}");

                dynamic propNode = comState.GetGUIPropertyNode(comPath, true);
                diag.AppendLine($"PropNode type: {((object)propNode).GetType().Name}");

                // Read existing user-defined count
                dynamic userDefBefore = propNode.UserDefined();
                int countBefore = userDefBefore.Count;
                diag.AppendLine($"UserDefined count before: {countBefore}");

                // Create property vector with one test property
                dynamic propVec = comState.ObjectFactory(_enumPropVec);
                diag.AppendLine($"PropVec type: {((object)propVec).GetType().Name}");

                dynamic prop = comState.ObjectFactory(_enumProp);
                diag.AppendLine($"Prop type: {((object)prop).GetType().Name}");

                prop.name = "test_name";
                prop.UserName = "Test Property";
                prop.value = "Hello from NavisVisualizer";
                propVec.Properties().Add(prop);

                int propCount = propVec.Properties().Count;
                diag.AppendLine($"PropVec.Properties().Count after Add: {propCount}");

                // Write
                propNode.SetUserDefined(0, CategoryInternalName, CategoryDisplayName, propVec);
                diag.AppendLine("SetUserDefined called (no exception)");

                // Verify: read back immediately
                dynamic userDefAfter = propNode.UserDefined();
                int countAfter = userDefAfter.Count;
                diag.AppendLine($"UserDefined count after: {countAfter}");

                if (countAfter > countBefore)
                {
                    // Read the last tab
                    dynamic lastTab = userDefAfter[countAfter];
                    diag.AppendLine($"Last tab Name: {lastTab.Name}");
                    diag.AppendLine($"Last tab UserName: {lastTab.UserName}");
                    dynamic tabProps = lastTab.Properties();
                    diag.AppendLine($"Tab properties count: {tabProps.Count}");
                    diag.AppendLine("SUCCESS: Property was written and verified!");
                }
                else
                {
                    diag.AppendLine("FAIL: UserDefined count did not increase.");
                    // List all existing tabs for debugging
                    for (int i = 1; i <= countAfter; i++)
                    {
                        try
                        {
                            dynamic tab = userDefAfter[i];
                            diag.AppendLine($"  Tab[{i}]: Name={tab.Name}, UserName={tab.UserName}");
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                diag.AppendLine($"EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            }

            return diag.ToString();
        }

        public int WriteSpoolProperties(
            List<SpoolData> spools,
            Dictionary<string, List<ModelItem>> searchResult,
            DateTime referenceDate)
        {
            dynamic comState = ComApiBridge.State;
            ResolveEnums();

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
                        dynamic comPath = ComApiBridge.ToInwOaPath(item);
                        dynamic propNode = comState.GetGUIPropertyNode(comPath, true);
                        dynamic propVec = comState.ObjectFactory(_enumPropVec);

                        AddProp(comState, propVec, "test_id", "Spool Number", spool.SpoolId);

                        string stageLabel = SpoolStageInfo.Labels.TryGetValue(stage, out var lbl)
                            ? lbl : stage.ToString();
                        AddProp(comState, propVec, "test_stage", "현재 단계", stageLabel);

                        propNode.SetUserDefined(0, CategoryInternalName, CategoryDisplayName, propVec);
                        written++;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.GetType().Name + ": " + ex.Message;
                    }
                }
            }

            if (attempted == 0)
                throw new Exception("속성 삽입 대상이 없습니다.");
            if (written == 0 && lastError != null)
                throw new Exception($"0/{attempted}건 실패: {lastError}");

            return written;
        }

        private void AddProp(dynamic comState, dynamic propVec, string internalName, string displayName, string value)
        {
            dynamic prop = comState.ObjectFactory(_enumProp);
            prop.name = internalName;
            prop.UserName = displayName;
            prop.value = value;
            propVec.Properties().Add(prop);
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

using System;
using System.Collections.Generic;
using System.Reflection;
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

                        // Build property vector
                        dynamic propVec = comState.ObjectFactory(_enumPropVec);

                        // Add simple test property first
                        AddProp(comState, propVec, "LcOaSpoolId", "Spool Number", spool.SpoolId);

                        if (!string.IsNullOrEmpty(spool.IsoNo))
                            AddProp(comState, propVec, "LcOaIsoNo", "ISO No", spool.IsoNo);

                        string stageLabel = SpoolStageInfo.Labels.TryGetValue(stage, out var lbl)
                            ? lbl : stage.ToString();
                        AddProp(comState, propVec, "LcOaStage", "현재 단계", stageLabel);

                        foreach (var s in SpoolStageInfo.OrderedStages)
                        {
                            string label = SpoolStageInfo.Labels[s];
                            string dateStr = spool.StageDates.TryGetValue(s, out var date) && date.HasValue
                                ? date.Value.ToString("yyyy-MM-dd") : "";
                            AddProp(comState, propVec, "LcOa" + s.ToString(), label, dateStr);
                        }

                        // Write to the item
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

            // Search loaded assemblies
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

            // Fallback: load from Navisworks directory
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

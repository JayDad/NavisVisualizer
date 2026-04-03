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
        private const string CategoryInternalName = "NavisVisualizer_SpoolData";

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
                        dynamic properties = BuildProperties(comState, spool, stage);
                        dynamic propNode = comState.GetGUIPropertyNode(comPath, true);
                        propNode.SetUserDefined(0, CategoryInternalName, CategoryDisplayName, properties);
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

        private static void ResolveEnums()
        {
            if (_enumPropVec != null) return;

            Type enumType = null;
            var diagInfo = new List<string>();

            // Strategy 1: Search all loaded assemblies for any type named nwEObjectType
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string asmName = asm.GetName().Name;
                if (asmName.Contains("Navisworks") && asmName.Contains("Interop"))
                {
                    diagInfo.Add($"Found assembly: {asmName}");
                    // Search all types for the enum
                    try
                    {
                        foreach (var t in asm.GetExportedTypes())
                        {
                            if (t.IsEnum && t.Name == "nwEObjectType")
                            {
                                enumType = t;
                                diagInfo.Add($"Found enum: {t.FullName}");
                                break;
                            }
                        }
                    }
                    catch (Exception ex) { diagInfo.Add($"GetExportedTypes failed: {ex.Message}"); }
                }
                if (enumType != null) break;
            }

            // Strategy 2: Force-load and search
            if (enumType == null)
            {
                try
                {
                    string navisDir = System.IO.Path.GetDirectoryName(
                        typeof(Autodesk.Navisworks.Api.Application).Assembly.Location);
                    diagInfo.Add($"Navisworks dir: {navisDir}");

                    string dllPath = System.IO.Path.Combine(navisDir, "Autodesk.Navisworks.Interop.ComApi.dll");
                    bool exists = System.IO.File.Exists(dllPath);
                    diagInfo.Add($"DLL exists: {exists}");

                    if (exists)
                    {
                        var asm = Assembly.LoadFrom(dllPath);
                        diagInfo.Add($"Loaded: {asm.FullName}");
                        foreach (var t in asm.GetExportedTypes())
                        {
                            if (t.IsEnum && t.Name.Contains("ObjectType"))
                            {
                                diagInfo.Add($"Enum found: {t.FullName}");
                                if (t.Name == "nwEObjectType")
                                {
                                    enumType = t;
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) { diagInfo.Add($"Strategy 2 failed: {ex.Message}"); }
            }

            if (enumType == null)
                throw new Exception(
                    "nwEObjectType enum을 찾을 수 없습니다.\n" +
                    string.Join("\n", diagInfo));

            _enumPropVec = Enum.Parse(enumType, "eObjectType_nwOaPropertyVec");
            _enumProp = Enum.Parse(enumType, "eObjectType_nwOaProperty");
        }

        private dynamic BuildProperties(dynamic comState, SpoolData spool, SpoolStage currentStage)
        {
            dynamic properties = comState.ObjectFactory(_enumPropVec, null, null);

            AddProperty(comState, properties, "Spool Number", spool.SpoolId);

            if (!string.IsNullOrEmpty(spool.IsoNo))
                AddProperty(comState, properties, "ISO No", spool.IsoNo);

            string stageLabel = SpoolStageInfo.Labels.TryGetValue(currentStage, out var lbl) ? lbl : currentStage.ToString();
            AddProperty(comState, properties, "현재 단계", stageLabel);

            foreach (var stage in SpoolStageInfo.OrderedStages)
            {
                string label = SpoolStageInfo.Labels[stage];
                if (spool.StageDates.TryGetValue(stage, out var date) && date.HasValue)
                    AddProperty(comState, properties, label, date.Value.ToString("yyyy-MM-dd"));
                else
                    AddProperty(comState, properties, label, "-");
            }

            return properties;
        }

        private void AddProperty(dynamic comState, dynamic properties, string name, string value)
        {
            dynamic prop = comState.ObjectFactory(_enumProp, null, null);
            prop.name = name;
            prop.UserName = name;
            prop.value = value;
            properties.Properties().Add(prop);
        }
    }
}

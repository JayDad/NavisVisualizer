using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using NavisVisualizer.Models;

namespace NavisVisualizer.Services
{
    public class UserDataService
    {
        private const string CategoryDisplayName = "Spool 실적";
        private const string CategoryInternalName = "NavisVisualizer_SpoolData";

        public int WriteSpoolProperties(
            List<SpoolData> spools,
            Dictionary<string, List<ModelItem>> searchResult,
            DateTime referenceDate)
        {
            dynamic comState = ComApiBridge.State;
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
                        comState.SetUserDefined(comPath, 0, CategoryInternalName, CategoryDisplayName, properties);
                        written++;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.GetType().Name + ": " + ex.Message;
                    }
                }
            }

            // Always report what happened
            if (attempted == 0)
                throw new Exception("속성 삽입 대상이 없습니다. (searchResult 매칭 0건)");
            if (written == 0 && lastError != null)
                throw new Exception($"0/{attempted}건 실패: {lastError}");

            return written;
        }

        private dynamic BuildProperties(dynamic comState, SpoolData spool, SpoolStage currentStage)
        {
            dynamic properties = comState.ObjectFactory(
                ResolveEnum("eObjectType_nwOaPropertyVec"), null, null);

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
            dynamic prop = comState.ObjectFactory(
                ResolveEnum("eObjectType_nwOaProperty"), null, null);
            prop.name = name;
            prop.UserName = name;
            prop.value = value;
            properties.Properties().Add(prop);
        }

        private static object _enumPropertyVec;
        private static object _enumProperty;

        private static object ResolveEnum(string valueName)
        {
            if (valueName == "eObjectType_nwOaPropertyVec" && _enumPropertyVec != null)
                return _enumPropertyVec;
            if (valueName == "eObjectType_nwOaProperty" && _enumProperty != null)
                return _enumProperty;

            // Try to find the enum type from loaded assemblies
            Type enumType = null;
            string[] possibleNames = new[]
            {
                "Autodesk.Navisworks.Interop.ComApi.nwEObjectType",
                "Autodesk.Navisworks.Api.Interop.ComApi.nwEObjectType",
            };

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var typeName in possibleNames)
                {
                    enumType = asm.GetType(typeName);
                    if (enumType != null) break;
                }
                if (enumType != null) break;
            }

            if (enumType != null)
            {
                var result = Enum.Parse(enumType, valueName);
                if (valueName.Contains("Vec")) _enumPropertyVec = result;
                else _enumProperty = result;
                return result;
            }

            // Fallback: hardcoded values for Navisworks 2022
            throw new Exception(
                $"nwEObjectType enum을 찾을 수 없습니다. " +
                $"Interop.ComApi DLL이 로드되었는지 확인하세요.");
        }
    }
}

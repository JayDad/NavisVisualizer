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

        /// <summary>
        /// Writes spool performance data as user-defined properties on matched model items.
        /// </summary>
        public int WriteSpoolProperties(
            List<SpoolData> spools,
            Dictionary<string, List<ModelItem>> searchResult,
            DateTime referenceDate)
        {
            dynamic comState = ComApiBridge.State;
            int written = 0;
            string lastError = null;

            foreach (var spool in spools)
            {
                if (!searchResult.TryGetValue(spool.SpoolId, out var items) || items.Count == 0)
                    continue;

                var stage = spool.GetStageAtDate(referenceDate);

                foreach (var item in items)
                {
                    try
                    {
                        dynamic comPath = ComApiBridge.ToInwOaPath(item);
                        dynamic properties = BuildProperties(comState, spool, stage);

                        // SetUserDefined is a method on State, not on Path
                        comState.SetUserDefined(
                            comPath,
                            0,
                            CategoryInternalName,
                            CategoryDisplayName,
                            properties);
                        written++;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                    }
                }
            }

            if (written == 0 && lastError != null)
                throw new Exception($"속성 삽입 실패: {lastError}");

            return written;
        }

        private dynamic BuildProperties(dynamic comState, SpoolData spool, SpoolStage currentStage)
        {
            // nwEObjectType.eObjectType_nwOaPropertyVec
            dynamic properties = comState.ObjectFactory(
                (dynamic)Enum.Parse(GetEnumType(comState), "eObjectType_nwOaPropertyVec"),
                null, null);

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
            // nwEObjectType.eObjectType_nwOaProperty
            dynamic prop = comState.ObjectFactory(
                (dynamic)Enum.Parse(GetEnumType(comState), "eObjectType_nwOaProperty"),
                null, null);
            prop.name = name;
            prop.UserName = name;
            prop.value = value;
            properties.Properties().Add(prop);
        }

        private Type _enumType;
        private Type GetEnumType(dynamic comState)
        {
            if (_enumType == null)
            {
                // Find nwEObjectType enum from the same assembly as the COM state
                Type stateType = ((object)comState).GetType();
                _enumType = stateType.Assembly.GetType("Autodesk.Navisworks.Interop.ComApi.nwEObjectType");
                if (_enumType == null)
                {
                    // Fallback: search all loaded assemblies
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        _enumType = asm.GetType("Autodesk.Navisworks.Interop.ComApi.nwEObjectType");
                        if (_enumType != null) break;
                    }
                }
            }
            return _enumType;
        }
    }
}

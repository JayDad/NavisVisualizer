using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
        /// Uses dynamic COM interop to avoid hard dependency on Interop.ComApi namespace.
        /// </summary>
        public int WriteSpoolProperties(
            List<SpoolData> spools,
            Dictionary<string, List<ModelItem>> searchResult,
            DateTime referenceDate)
        {
            dynamic comState = ComApiBridge.State;
            int written = 0;

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
                        comPath.UserDefined.SetUserDefined(
                            0,
                            CategoryInternalName,
                            CategoryDisplayName,
                            properties);
                        written++;
                    }
                    catch { }
                }
            }

            return written;
        }

        /// <summary>
        /// Removes spool performance data from all model items.
        /// </summary>
        public void ClearSpoolProperties(Document doc)
        {
            foreach (var item in doc.Models.RootItemDescendantsAndSelf)
            {
                try
                {
                    dynamic comPath = ComApiBridge.ToInwOaPath(item);
                    dynamic userDefined = comPath.UserDefined;
                    int count = userDefined.Count;
                    for (int i = 1; i <= count; i++)
                    {
                        dynamic tab = userDefined[i];
                        if ((string)tab.UserName == CategoryDisplayName ||
                            (string)tab.Name == CategoryInternalName)
                        {
                            userDefined.RemoveUserDefined(i);
                            break;
                        }
                    }
                }
                catch { }
            }
        }

        private dynamic BuildProperties(dynamic comState, SpoolData spool, SpoolStage currentStage)
        {
            // eObjectType_nwOaPropertyVec = 1
            dynamic properties = comState.ObjectFactory(1, null, null);

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
            // eObjectType_nwOaProperty = 2
            dynamic prop = comState.ObjectFactory(2, null, null);
            prop.name = name;
            prop.UserName = name;
            prop.value = value;
            properties.Properties().Add(prop);
        }
    }
}

using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Interop.ComApi;
using NavisVisualizer.Models;

namespace NavisVisualizer.Services
{
    public class UserDataService
    {
        private const string CategoryName = "Spool 실적";
        private const string InternalCategoryName = "NavisVisualizer_SpoolData";

        /// <summary>
        /// Writes spool performance data as user-defined properties on matched model items.
        /// </summary>
        public int WriteSpoolProperties(
            List<SpoolData> spools,
            Dictionary<string, List<ModelItem>> searchResult,
            DateTime referenceDate)
        {
            var comState = ComApiBridge.State;
            int written = 0;

            foreach (var spool in spools)
            {
                if (!searchResult.TryGetValue(spool.SpoolId, out var items) || items.Count == 0)
                    continue;

                var stage = spool.GetStageAtDate(referenceDate);
                var properties = BuildProperties(comState, spool, stage);

                foreach (var item in items)
                {
                    try
                    {
                        var comPath = ComApiBridge.ToInwOaPath(item);
                        comPath.UserDefined.SetUserDefined(
                            0,
                            InternalCategoryName,
                            CategoryName,
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
                    var comPath = ComApiBridge.ToInwOaPath(item);
                    var userDefined = comPath.UserDefined;
                    // Remove by setting empty property vector
                    for (int i = 1; i <= userDefined.Count; i++)
                    {
                        var tab = userDefined[i];
                        if (tab.UserName == CategoryName || tab.Name == InternalCategoryName)
                        {
                            userDefined.RemoveUserDefined(i);
                            break;
                        }
                    }
                }
                catch { }
            }
        }

        private InwOaPropertyVec BuildProperties(
            InwOpState comState,
            SpoolData spool,
            SpoolStage currentStage)
        {
            var properties = (InwOaPropertyVec)comState.ObjectFactory(
                nwEObjectType.eObjectType_nwOaPropertyVec, null, null);

            // Spool ID
            AddProperty(comState, properties, "Spool Number", spool.SpoolId);

            // ISO No
            if (!string.IsNullOrEmpty(spool.IsoNo))
                AddProperty(comState, properties, "ISO No", spool.IsoNo);

            // Current stage
            string stageLabel = SpoolStageInfo.Labels.TryGetValue(currentStage, out var lbl) ? lbl : currentStage.ToString();
            AddProperty(comState, properties, "현재 단계", stageLabel);

            // All stage dates
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

        private void AddProperty(
            InwOpState comState,
            InwOaPropertyVec properties,
            string name,
            string value)
        {
            var prop = (InwOaProperty)comState.ObjectFactory(
                nwEObjectType.eObjectType_nwOaProperty, null, null);
            prop.name = name;
            prop.UserName = name;
            prop.value = value;
            properties.Properties().Add(prop);
        }
    }
}

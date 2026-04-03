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
                diag.AppendLine($"Enum: PropVec={_enumPropVec}, Prop={_enumProp}");

                dynamic comPath = ComApiBridge.ToInwOaPath(item);
                Type pathType = ((object)comPath).GetType();
                diag.AppendLine($"Path type: {pathType.Name}");

                // List interfaces on comPath
                foreach (var iface in pathType.GetInterfaces())
                {
                    if (iface.Name.Contains("Inw"))
                        diag.AppendLine($"  Path iface: {iface.Name}");
                }

                dynamic propNode = comState.GetGUIPropertyNode(comPath, true);
                Type nodeType = ((object)propNode).GetType();
                diag.AppendLine($"PropNode type: {nodeType.Name}");

                // List interfaces on propNode
                foreach (var iface in nodeType.GetInterfaces())
                {
                    if (iface.Name.Contains("Inw"))
                        diag.AppendLine($"  Node iface: {iface.Name}");
                }

                // Try to find SetUserDefined via interfaces
                bool hasSetUserDefined = false;
                bool hasUserDefined = false;
                foreach (var iface in nodeType.GetInterfaces())
                {
                    foreach (var method in iface.GetMethods())
                    {
                        if (method.Name == "SetUserDefined") hasSetUserDefined = true;
                        if (method.Name == "UserDefined") hasUserDefined = true;
                    }
                }
                diag.AppendLine($"HasSetUserDefined: {hasSetUserDefined}");
                diag.AppendLine($"HasUserDefined: {hasUserDefined}");

                // If InwGUIPropertyNode2 interface exists, try casting
                Type node2Type = null;
                foreach (var iface in nodeType.GetInterfaces())
                {
                    if (iface.Name.Contains("PropertyNode2") || iface.Name.Contains("GUIPropertyNode2"))
                    {
                        node2Type = iface;
                        diag.AppendLine($"Found v2 interface: {iface.FullName}");
                        break;
                    }
                }

                if (node2Type != null)
                {
                    // Use reflection to call SetUserDefined via the v2 interface
                    dynamic propVec = comState.ObjectFactory(_enumPropVec);
                    dynamic prop = comState.ObjectFactory(_enumProp);
                    prop.name = "test_name";
                    prop.UserName = "Test Property";
                    prop.value = "Hello";
                    propVec.Properties().Add(prop);
                    diag.AppendLine($"PropVec created, count: {propVec.Properties().Count}");

                    // Call via reflection
                    var setMethod = node2Type.GetMethod("SetUserDefined");
                    if (setMethod != null)
                    {
                        diag.AppendLine($"SetUserDefined params: {string.Join(", ", Array.ConvertAll(setMethod.GetParameters(), p => p.ParameterType.Name + " " + p.Name))}");
                        setMethod.Invoke((object)propNode, new object[] { 0, CategoryInternalName, CategoryDisplayName, (object)propVec });
                        diag.AppendLine("SetUserDefined called via reflection!");

                        var getMethod = node2Type.GetMethod("UserDefined");
                        if (getMethod != null)
                        {
                            dynamic userDef = getMethod.Invoke((object)propNode, null);
                            diag.AppendLine($"UserDefined count after: {userDef.Count}");
                        }
                    }
                    else
                    {
                        diag.AppendLine("SetUserDefined method NOT found on v2 interface");
                    }
                }
                else
                {
                    diag.AppendLine("No v2 interface found. Listing all interface methods:");
                    foreach (var iface in nodeType.GetInterfaces())
                    {
                        if (!iface.Name.Contains("Inw")) continue;
                        foreach (var m in iface.GetMethods())
                            diag.AppendLine($"  {iface.Name}.{m.Name}");
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

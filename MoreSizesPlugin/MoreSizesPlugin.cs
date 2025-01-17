﻿using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using BepInEx;
using BepInEx.Configuration;
using Newtonsoft.Json;
using RadialUI;
using Bounce.Unmanaged;
using HarmonyLib;
using MoreSizesPlugin.Patches;
using PluginUtilities;
using System.IO;
using System.Linq;
using RPCPlugin.RPC;
using MoreSizesPlugin.Consumer.Messages;
using System.Collections;
using System;

namespace MoreSizesPlugin
{
    [BepInPlugin(Guid, "More Sizes Plug-In", Version)]
    [BepInDependency(RadialUIPlugin.Guid)]
    [BepInDependency(SetInjectionFlag.Guid)]
    [BepInDependency(RPCPlugin.RPCPlugin.Guid)]
    [BepInDependency("org.lordashes.plugins.assetdata", BepInDependency.DependencyFlags.SoftDependency)]
    public class MoreSizesPlugin : BaseUnityPlugin
    {
        // constants
        private const string Guid = "org.hollofox.plugins.MoreSizesPlugin";
        private const string Version = "2.4.0.0";
        private static CreatureGuid _selectedCreature;

        private static MoreSizesPlugin _self = null;
        private readonly float[] coreSizes = new[] { 0.5f, 1f, 2f, 3f, 4f };
        private static float _restorationDelay = 1.0f;
        private MethodInfo setMethod = null;
        private MethodInfo clearMethod = null;

        // Config
        private ConfigEntry<string> _customSizes;


        /// <summary>
        /// Awake plugin
        /// </summary>
        void Awake()
        {
            _self = this;

            Logger.LogInfo("In Awake for More Sizes");
            _customSizes = Config.Bind("Sizes", "List", JsonConvert.SerializeObject(new List<float>
            {
                0.5f,
                0.75f,
                1f,
                1.5f,
                2f,
                3f,
                4f,
                6f,
                8f,
                10f,
                15f,
                20f,
                25f,
                30f,
            }));

            _restorationDelay = Config.Bind("Settings", "Restoration delay upon board dload", 1.0f).Value;
            var harmony = new Harmony(Guid);
            harmony.PatchAll();
            Logger.LogDebug("MoreSizes Plug-in loaded");

            ModdingTales.ModdingUtils.AddPluginToMenuList(this, "Hollofoxes'");

            RadialUIPlugin.HideDefaultEmotesGMItem(Guid, "Set Size");
            RadialUIPlugin.AddCustomButtonGMSubmenu("Set Size",
                new MapMenu.ItemArgs
                {
                    Action = HandleSubmenus,
                    //Icon = LoadEmbeddedTexture("creaturesize.png"),
                    CloseMenuOnActivate = false,
                    Title = "Set Size",
                }
                , Reporter);

            AddAssetDataPluginPersistenceIfAvailable();
        }

        private void AddAssetDataPluginPersistenceIfAvailable()
        {
            Type adp = Type.GetType("LordAshes.AssetDataPlugin, AssetDataPlugin");
            if(adp != null)
            {
                MethodInfo subscribeMethod = adp.GetRuntimeMethods().Where(m => m.Name == "SubscribeViaReflection").ElementAt(0);
                subscribeMethod.Invoke(null, new object[] { MoreSizesPlugin.Guid + ".size", this.GetType().AssemblyQualifiedName, "RestoreSize" });
                setMethod = adp.GetRuntimeMethods().Where(m => m.Name == "SetInfo").ElementAt(0);
                clearMethod = adp.GetRuntimeMethods().Where(m => m.Name == "ClearInfo").ElementAt(0);
                Logger.LogDebug("More Size Plugin size persistence handled by Asset Data Plugin." );
            }
            else
            {
                Logger.LogDebug("More Size Plugin size persistence unavailable. Missing Asset Data Plugin.");
            }
        }

        public static void RestoreSize(string action, string identity, string key, object previous, object value)
        {
            _self.Logger.LogDebug(DateTime.UtcNow+ ": Preparing to restore custom size " + value + " on creature id " + identity);
            _self.StartCoroutine(DelayedRestoreSize(new NGuid(identity).ToHexString(), float.Parse(value.ToString(), System.Globalization.CultureInfo.InvariantCulture), _restorationDelay));
        }

        private static IEnumerator DelayedRestoreSize(string cid, float actualSize, float delay)
        {
            yield return new WaitForSeconds(delay);
            _self.Logger.LogDebug(DateTime.UtcNow + ": Restoring custom size " + actualSize + " on creature id " + cid);
            RPCInstance.SendMessage(new ScaleMini
            {
                size = actualSize,
                cid = cid
            });
        }

        private static Sprite LoadEmbeddedTexture(string texturePath)
        {
            Assembly _assembly = Assembly.GetExecutingAssembly();
            Stream _stream = _assembly.GetManifestResourceStream(typeof(MoreSizesPlugin).Namespace + "." + texturePath.Replace("/", "."));
            byte[] fileData;
            using (MemoryStream ms = new MemoryStream())
            {
                _stream.CopyTo(ms);
                fileData = ms.ToArray();
            }
            Texture2D tex = new Texture2D(1, 1);
            tex.LoadImage(fileData);
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        private void HandleSubmenus(MapMenuItem arg1, object arg2)
        {
            CreaturePresenter.TryGetAsset(_selectedCreature, out CreatureBoardAsset asset);
            OpenResizeMini(arg1, arg2);
        }

        private void OpenResizeMini(MapMenuItem arg1, object arg2)
        {
            CreaturePresenter.TryGetAsset(_selectedCreature, out CreatureBoardAsset asset);
            var c = asset;
            MapMenu mapMenu = MapMenuManager.OpenMenu(c.transform.position + Vector3.up * CreatureMenuBoardPatch._hitHeightDif, true);
            var sizes = JsonConvert.DeserializeObject<List<float>>(_customSizes.Value);
            foreach (var size in sizes)
            {
                if (size < 1) AddSize(mapMenu, size, Icons.GetIconSprite("05x05"));
                else if (size < 2) AddSize(mapMenu, size, Icons.GetIconSprite("1x1"));
                else if (size < 3) AddSize(mapMenu, size, Icons.GetIconSprite("2x2"));
                else if (size < 4) AddSize(mapMenu, size, Icons.GetIconSprite("3x3"));
                else AddSize(mapMenu, size, Icons.GetIconSprite("4x4"));
            }
        }

        private void AddSize(MapMenu mapMenu, float x, Sprite icon = null)
        {
            mapMenu.AddItem(new MapMenu.ItemArgs
            {
                Title = $"{x}x{x}",
                Action = Menu_Scale,
                Obj = x,
                CloseMenuOnActivate = true,
                Icon = icon
            });
        }

        private bool Reporter(NGuid arg1, NGuid arg2)
        {
            _selectedCreature = new CreatureGuid(arg2);
            return true;
        }

        private void Menu_Scale(MapMenuItem item, object obj)
        {   
            float actualSize = (float)obj;
            string cid = _selectedCreature.Value.ToHexString();
            
            if (!coreSizes.Contains(actualSize))
            {
                Logger.LogInfo($"New message: {actualSize}");
                RPCInstance.SendMessage(new ScaleMini
                {
                    size = actualSize,
                    cid = cid
                });
                if (setMethod != null)
                {
                    Logger.LogDebug("Saving custom size " + actualSize + " on creature id " + cid);
                    setMethod.Invoke(null,new object[] { _selectedCreature.Value.ToString(), MoreSizesPlugin.Guid + ".size", actualSize.ToString(), false});
                }
            }
            else
            {
                CreatureManager.SetCreatureScale(_selectedCreature, 0, actualSize);
                if (clearMethod != null)
                {
                    Logger.LogDebug("Clearing custom size " + actualSize + " on creature id " + cid);
                    clearMethod.Invoke(null, new object[] { _selectedCreature.Value.ToString(), MoreSizesPlugin.Guid, false });
                }
            }
        }
    }
}

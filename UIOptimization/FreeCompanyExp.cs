using System;
using System.Numerics;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using KamiToolKit.Nodes;

namespace DailyRoutines.ModulesPublic;

public unsafe class FreeCompanyExp : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = "更精确的部队经验",
        Description = "在部队界面显示部队当前经验和升级所需经验的精确数值",
        Category = ModuleCategories.UIOptimization,
        Author = ["Cothlory"]
    };

    private static TextNode? ExpTextNode;
    private static uint lastCurrentExp = 0;
    private static uint lastMaxExp = 0;
    private static string originalLevelText = "";
    private static ushort originalExpBar14Width = 0;
    private static ushort originalExpBar15Width = 0;
    private static float originalExpBar14X = 0;
    private static float originalExpBar15X = 0;
    private static float originalExpLevelTextX = 0;

    protected override void Init()
    {
        FrameworkManager.Register(OnUpdate, throttleMS: 1_000);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "FreeCompany", OnFreeCompanyAddonSetup);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "FreeCompany", OnFreeCompanyAddonFinalize);
        
        if (IsAddonAndNodesReady(FreeCompany))
            OnFreeCompanyAddonSetup(AddonEvent.PostSetup, null);
    }

    protected override void Uninit()
    {
        FrameworkManager.Unregister(OnUpdate);
        DService.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "FreeCompany", OnFreeCompanyAddonSetup);
        DService.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "FreeCompany", OnFreeCompanyAddonFinalize);
        RestoreOriginalLevelText();
        RestoreOriginalUI();
        ExpTextNode?.Dispose();
        ExpTextNode = null;
        base.Uninit();
    }

    private void OnFreeCompanyAddonSetup(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)FreeCompany;
        CreateExpNodes(addon);
        UpdateExpDisplay();
    }

    private void OnFreeCompanyAddonFinalize(AddonEvent type, AddonArgs args)
    {
        RestoreOriginalLevelText();
        ExpTextNode?.Dispose();
        ExpTextNode = null;
        lastCurrentExp = 0;
        lastMaxExp = 0;
    }

    private void OnUpdate(Dalamud.Plugin.Services.IFramework framework)
    {
        if (!DService.ClientState.IsLoggedIn) return;
        
        if (IsAddonAndNodesReady(FreeCompany) && ExpTextNode != null)
        {
            TryRefreshFCData();
            var (currentExp, maxExp, level) = GetCurrentFCData();
            if (currentExp != lastCurrentExp || maxExp != lastMaxExp)
            {
                lastCurrentExp = currentExp;
                lastMaxExp = maxExp;
                UpdateExpDisplay();
            }
        }
    }

    private (uint currentExp, uint maxExp, byte level) GetCurrentFCData()
    {
        var stage = AtkStage.Instance();
        if (stage == null) return (0, 0, 0);

        var fcExchangeArray = stage->GetNumberArrayData(NumberArrayType.FreeCompanyExchange);
        if (fcExchangeArray == null || fcExchangeArray->AtkArrayData.Size < 10)
            return (0, 0, 0);
            
        var currentExp = (uint)fcExchangeArray->IntArray[6];
        var maxExp = (uint)fcExchangeArray->IntArray[7];
        var level = (byte)fcExchangeArray->IntArray[4];

        return (currentExp, maxExp, level);
    }

    private unsafe void CreateExpNodes(AtkUnitBase* addon)
    {
        if (ExpTextNode != null) return;
        
        var expLevelTextNode = addon->GetNodeById(13);
        if (expLevelTextNode != null && expLevelTextNode->Type == NodeType.Text)
        {
            var textNode = (AtkTextNode*)expLevelTextNode;
            originalLevelText = textNode->NodeText.ToString();
        }
        
        ModifyExpBar(addon);
        UpdateExpDisplay();
    }
    
    private unsafe void ModifyExpBar(AtkUnitBase* addon)
    {
        var currentExpBarNode = addon->GetNodeById(14);
        var maxExpBarNode = addon->GetNodeById(15);
        var expLevelTextNode = addon->GetNodeById(13);
        if (currentExpBarNode != null)
        {
            originalExpBar14Width = currentExpBarNode->Width;
            originalExpBar14X = currentExpBarNode->X;
        }
        if (maxExpBarNode != null)
        {
            originalExpBar15Width = maxExpBarNode->Width;
            originalExpBar15X = maxExpBarNode->X;
        }
        if (expLevelTextNode != null)
        {
            originalExpLevelTextX = expLevelTextNode->X;
            if (string.IsNullOrEmpty(originalLevelText) && expLevelTextNode->Type == NodeType.Text)
            {
                var textNode = (AtkTextNode*)expLevelTextNode;
                originalLevelText = textNode->NodeText.ToString();
            }
        }
        
        var originalExpBarWidth = currentExpBarNode != null ? currentExpBarNode->Width : 0;
        var expBarOffset = originalExpBarWidth;
        
        // 进度条
        if (currentExpBarNode != null)
        {
            var originalWidth = currentExpBarNode->Width;
            var newWidth = (ushort)(originalWidth * 2f);
            var originalX = currentExpBarNode->X;
            var newX = (short)(originalX - originalWidth);
            currentExpBarNode->SetWidth(newWidth);
            currentExpBarNode->SetXFloat(newX);
            var nineGridNode = (AtkNineGridNode*)currentExpBarNode;
            nineGridNode->AtkResNode.SetWidth(newWidth);
            nineGridNode->AtkResNode.SetXFloat(newX);
            currentExpBarNode->DrawFlags |= 0x1;
        }
        
        // 背景条
        if (maxExpBarNode != null)
        {
            var originalWidth = maxExpBarNode->Width;
            var newWidth = (ushort)(originalWidth * 2f);
            var originalX = maxExpBarNode->X;
            var newX = (short)(originalX - originalWidth);
            maxExpBarNode->SetWidth(newWidth);
            maxExpBarNode->SetXFloat(newX);
            var nineGridNode = (AtkNineGridNode*)maxExpBarNode;
            nineGridNode->AtkResNode.SetWidth(newWidth);
            nineGridNode->AtkResNode.SetXFloat(newX);
            maxExpBarNode->DrawFlags |= 0x1;
        }
        
        // 等级
        if (expLevelTextNode != null && expLevelTextNode->Type == NodeType.Text)
        {
            var originalX = expLevelTextNode->X;
            var newX = (short)(originalX - expBarOffset);
            expLevelTextNode->SetXFloat(newX);
            expLevelTextNode->DrawFlags |= 0x1;
        }
    }
    
    private unsafe void UpdateExpDisplay()
    {
        if (!IsAddonAndNodesReady(FreeCompany)) return;
        
        var addon = (AtkUnitBase*)FreeCompany;
        var expLevelTextNode = addon->GetNodeById(13);
        if (expLevelTextNode == null || expLevelTextNode->Type != NodeType.Text) return;
        
        var (currentExp, maxExp, level) = GetCurrentFCData();
        if (currentExp == 0 && maxExp == 0) return;
        
        var progress = maxExp > 0 ? (float)currentExp / maxExp * 100 : 0f;
        var expText = $"经验值 {currentExp:N0}/{maxExp:N0} ({progress:F1}%)";
        var newText = $"{originalLevelText}    {expText}";
        var textNode = (AtkTextNode*)expLevelTextNode;
        textNode->SetText(newText);
        expLevelTextNode->DrawFlags |= 0x1;
    }
    
    private unsafe void RestoreOriginalUI()
    {
        if (!IsAddonAndNodesReady(FreeCompany)) return;
        var addon = (AtkUnitBase*)FreeCompany;
        var currentExpBarNode = addon->GetNodeById(14);
        var maxExpBarNode = addon->GetNodeById(15);
        var expLevelTextNode = addon->GetNodeById(13);
        if (currentExpBarNode != null && originalExpBar14Width > 0)
        {
            currentExpBarNode->SetWidth(originalExpBar14Width);
            currentExpBarNode->SetXFloat(originalExpBar14X);
            var nineGridNode = (AtkNineGridNode*)currentExpBarNode;
            nineGridNode->AtkResNode.SetWidth(originalExpBar14Width);
            nineGridNode->AtkResNode.SetXFloat(originalExpBar14X);
            currentExpBarNode->DrawFlags |= 0x1;
        }
        if (maxExpBarNode != null && originalExpBar15Width > 0)
        {
            maxExpBarNode->SetWidth(originalExpBar15Width);
            maxExpBarNode->SetXFloat(originalExpBar15X);
            var nineGridNode = (AtkNineGridNode*)maxExpBarNode;
            nineGridNode->AtkResNode.SetWidth(originalExpBar15Width);
            nineGridNode->AtkResNode.SetXFloat(originalExpBar15X);
            maxExpBarNode->DrawFlags |= 0x1;
        }
        if (expLevelTextNode != null && expLevelTextNode->Type == NodeType.Text && originalExpBar14Width > 0)
        {
            expLevelTextNode->SetXFloat(originalExpLevelTextX);
            if (!string.IsNullOrEmpty(originalLevelText))
            {
                var textNode = (AtkTextNode*)expLevelTextNode;
                textNode->SetText(originalLevelText);
            }
            expLevelTextNode->DrawFlags |= 0x1;
        }
        originalExpBar14Width = 0;
        originalExpBar15Width = 0;
        originalExpBar14X = 0f;
        originalExpBar15X = 0f;
        originalExpLevelTextX = 0f;
        originalLevelText = "";
    }
    
    private unsafe void RestoreOriginalLevelText()
    {
        if (!IsAddonAndNodesReady(FreeCompany) || string.IsNullOrEmpty(originalLevelText)) return;
        
        var addon = (AtkUnitBase*)FreeCompany;
        var expLevelTextNode = addon->GetNodeById(13);
        if (expLevelTextNode != null && expLevelTextNode->Type == NodeType.Text)
        {
            var textNode = (AtkTextNode*)expLevelTextNode;
            textNode->SetText(originalLevelText);
            expLevelTextNode->DrawFlags |= 0x1;
        }
    }

    private void TryRefreshFCData()
    {
        var infoModule = InfoModule.Instance();
        if (infoModule == null) return;

        var fcInfoProxy = infoModule->GetInfoProxyById(InfoProxyId.FreeCompany);
        if (fcInfoProxy == null) return;
        var localPlayer = DService.ClientState.LocalPlayer;
        if (localPlayer != null && localPlayer.EntityId != 0)
        {
            var fcProxy = (InfoProxyFreeCompany*)fcInfoProxy;
            fcProxy->RequestDataForCharacter(localPlayer.EntityId);
        }
    }
}

using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;

namespace DailyRoutines.ModulesPublic;

public unsafe class AccurateFreeCompanyExp : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = "精确部队经验",
        Description = "在部队界面显示部队经验的精确数值",
        Category = ModuleCategories.UIOptimization,
        Author = ["Cothlory"]
    };

    private static TextNode? ExpTextNode;
    private static uint lastCurrentExp;
    private static uint lastMaxExp;
    private static string originalLevelText = string.Empty;
    private static ushort originalExpBar14Width;
    private static ushort originalExpBar15Width;
    private static float originalExpBar14X;
    private static float originalExpBar15X;
    private static float originalExpLevelTextX;

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
        
        RestoreOriginalUI();
        ExpTextNode?.Dispose();
        ExpTextNode = null;
        
        base.Uninit();
    }

    private void OnFreeCompanyAddonSetup(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)FreeCompany;
        SetupExpDisplay(addon);
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
        if (!DService.ClientState.IsLoggedIn || !IsAddonAndNodesReady(FreeCompany)) 
            return;

        TryRefreshFCData();
        var (currentExp, maxExp, level) = GetCurrentFCData();
        
        if (currentExp != lastCurrentExp || maxExp != lastMaxExp)
        {
            lastCurrentExp = currentExp;
            lastMaxExp = maxExp;
            UpdateExpDisplay();
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

    private unsafe void SetupExpDisplay(AtkUnitBase* addon)
    {
        if (ExpTextNode != null) return;
        
        SaveOriginalLevelText(addon);
        ModifyExpBar(addon);
        UpdateExpDisplay();
    }

    private unsafe void SaveOriginalLevelText(AtkUnitBase* addon)
    {
        var expLevelTextNode = addon->GetNodeById(13);
        if (expLevelTextNode == null || expLevelTextNode->Type != NodeType.Text) return;

        var textNode = (AtkTextNode*)expLevelTextNode;
        var currentText = textNode->NodeText.ToString();
        
        if (string.IsNullOrEmpty(originalLevelText) || !currentText.Contains("经验值"))
            originalLevelText = currentText;
    }
    
    private unsafe void ModifyExpBar(AtkUnitBase* addon)
    {
        var currentExpBarNode = addon->GetNodeById(14);
        var maxExpBarNode = addon->GetNodeById(15);
        var expLevelTextNode = addon->GetNodeById(13);
        SaveOriginalBarStates(currentExpBarNode, maxExpBarNode, expLevelTextNode);
        var originalExpBarWidth = currentExpBarNode != null ? currentExpBarNode->Width : (ushort)0;
        ModifyExpBarNode(currentExpBarNode, originalExpBarWidth);
        ModifyExpBarNode(maxExpBarNode, originalExpBarWidth);
        ModifyLevelTextPosition(expLevelTextNode, originalExpBarWidth);
    }

    private unsafe void SaveOriginalBarStates(AtkResNode* currentExpBarNode, AtkResNode* maxExpBarNode, AtkResNode* expLevelTextNode)
    {
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
            originalExpLevelTextX = expLevelTextNode->X;
    }

    private unsafe void ModifyExpBarNode(AtkResNode* barNode, ushort originalWidth)
    {
        if (barNode == null) return;

        var newWidth = (ushort)(originalWidth * 2f);
        var newX = (short)(barNode->X - originalWidth);
        
        barNode->SetWidth(newWidth);
        barNode->SetXFloat(newX);
        
        var nineGridNode = (AtkNineGridNode*)barNode;
        nineGridNode->AtkResNode.SetWidth(newWidth);
        nineGridNode->AtkResNode.SetXFloat(newX);
        
        barNode->DrawFlags |= 0x1;
    }

    private unsafe void ModifyLevelTextPosition(AtkResNode* textNode, ushort expBarOffset)
    {
        if (textNode == null || textNode->Type != NodeType.Text) return;

        var newX = (short)(textNode->X - expBarOffset);
        textNode->SetXFloat(newX);
        textNode->DrawFlags |= 0x1;
    }
    
    private unsafe void UpdateExpDisplay()
    {
        if (!IsAddonAndNodesReady(FreeCompany)) return;
        
        var addon = (AtkUnitBase*)FreeCompany;
        var expLevelTextNode = addon->GetNodeById(13);
        if (expLevelTextNode == null || expLevelTextNode->Type != NodeType.Text) return;
        var (currentExp, maxExp, level) = GetCurrentFCData();
        if (currentExp == 0 && maxExp == 0) return;
        EnsureOriginalLevelText(expLevelTextNode);
        var progress = maxExp > 0 ? (float)currentExp / maxExp * 100 : 0f;
        var expText = $"经验值 {currentExp:N0}/{maxExp:N0} ({progress:F1}%)";
        var newText = $"{originalLevelText}    {expText}";
        var textNode = (AtkTextNode*)expLevelTextNode;
        textNode->SetText(newText);
        expLevelTextNode->DrawFlags |= 0x1;
    }

    private unsafe void EnsureOriginalLevelText(AtkResNode* expLevelTextNode)
    {
        if (!string.IsNullOrEmpty(originalLevelText)) return;
        var textNode = (AtkTextNode*)expLevelTextNode;
        var currentText = textNode->NodeText.ToString();
        if (!currentText.Contains("经验值"))
            originalLevelText = currentText;
    }
    
    private unsafe void RestoreOriginalUI()
    {
        if (!IsAddonAndNodesReady(FreeCompany)) return;
        var addon = (AtkUnitBase*)FreeCompany;
        var currentExpBarNode = addon->GetNodeById(14);
        var maxExpBarNode = addon->GetNodeById(15);
        var expLevelTextNode = addon->GetNodeById(13);
        RestoreExpBarNode(currentExpBarNode, originalExpBar14Width, originalExpBar14X);
        RestoreExpBarNode(maxExpBarNode, originalExpBar15Width, originalExpBar15X);
        RestoreLevelTextNode(expLevelTextNode);
        ClearOriginalStates();
    }

    private unsafe void RestoreExpBarNode(AtkResNode* barNode, ushort originalWidth, float originalX)
    {
        if (barNode == null || originalWidth == 0) return;
        barNode->SetWidth(originalWidth);
        barNode->SetXFloat(originalX);
        var nineGridNode = (AtkNineGridNode*)barNode;
        nineGridNode->AtkResNode.SetWidth(originalWidth);
        nineGridNode->AtkResNode.SetXFloat(originalX);
        barNode->DrawFlags |= 0x1;
    }

    private unsafe void RestoreLevelTextNode(AtkResNode* textNode)
    {
        if (textNode == null || textNode->Type != NodeType.Text || originalExpBar14Width == 0) return;
        textNode->SetXFloat(originalExpLevelTextX);
        if (!string.IsNullOrEmpty(originalLevelText))
        {
            var atkTextNode = (AtkTextNode*)textNode;
            atkTextNode->SetText(originalLevelText);
        }
        textNode->DrawFlags |= 0x1;
    }

    private void ClearOriginalStates()
    {
        originalExpBar14Width = 0;
        originalExpBar15Width = 0;
        originalExpBar14X = 0f;
        originalExpBar15X = 0f;
        originalExpLevelTextX = 0f;
        originalLevelText = string.Empty;
    }
    
    private unsafe void RestoreOriginalLevelText()
    {
        if (!IsAddonAndNodesReady(FreeCompany) || string.IsNullOrEmpty(originalLevelText)) return;
        var addon = (AtkUnitBase*)FreeCompany;
        var expLevelTextNode = addon->GetNodeById(13);
        if (expLevelTextNode == null || expLevelTextNode->Type != NodeType.Text) return;
        var textNode = (AtkTextNode*)expLevelTextNode;
        textNode->SetText(originalLevelText);
        expLevelTextNode->DrawFlags |= 0x1;
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

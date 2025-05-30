﻿// File: Assets/Scripts/UI/RewardScreenManager.cs

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

public class RewardScreenManager : MonoBehaviour
{
    [Header("UI Elements")]
    public GameObject rewardUI;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI currentWaveText;
    public TextMeshProUGUI nextWaveText;
    public TextMeshProUGUI enemiesKilledText;

    [Header("Spell Reward UI")]
    public Image spellIcon;
    public TextMeshProUGUI spellNameText;
    public TextMeshProUGUI spellDescriptionText;
    public TextMeshProUGUI damageValueText;
    public TextMeshProUGUI manaValueText;

    [Header("Buttons")]
    public Button acceptSpellButton;
    public Button nextWaveButton;

    private EnemySpawner spawner;
    private SpellCaster playerSpellCaster;
    private GameManager.GameState prevState;
    private Coroutine rewardCoroutine;
    private Spell offeredSpell;
    private Dictionary<string, JObject> spellCatalog;

    void Start()
    {
        spawner = Object.FindFirstObjectByType<EnemySpawner>();
        if (rewardUI != null) rewardUI.SetActive(false);
        if (acceptSpellButton != null) acceptSpellButton.onClick.AddListener(AcceptSpell);
        if (nextWaveButton != null) nextWaveButton.onClick.AddListener(OnNextWaveClicked);

        prevState = GameManager.Instance.state;

        // load spells.json
        var ta = Resources.Load<TextAsset>("spells");
        if (ta != null)
            spellCatalog = JsonConvert.DeserializeObject<Dictionary<string, JObject>>(ta.text);
        else
            Debug.LogError("RewardScreenManager: spells.json not found in Resources!");
    }

    void Update()
    {
        var state = GameManager.Instance.state;
        if (state == prevState) return;

        // show reward screen on every WAVEEND (including endless)
        if (state == GameManager.GameState.WAVEEND)
        {
            if (rewardCoroutine != null) StopCoroutine(rewardCoroutine);
            rewardCoroutine = StartCoroutine(ShowRewardScreen());
        }
        else
        {
            if (rewardUI != null) rewardUI.SetActive(false);
        }

        prevState = state;
    }

    IEnumerator ShowRewardScreen()
    {
        yield return new WaitForSeconds(0.25f);

        if (titleText != null) titleText.text = "You Survived!";
        if (currentWaveText != null) currentWaveText.text = $"Current Wave: {spawner.currentWave - 1}";
        if (nextWaveText != null) nextWaveText.text = $"Next Wave: {spawner.currentWave}";
        if (enemiesKilledText != null) enemiesKilledText.text = $"Enemies Killed: {spawner.lastWaveEnemyCount}";

        GenerateSpellReward();

        if (rewardUI != null) rewardUI.SetActive(true);
        if (acceptSpellButton != null) acceptSpellButton.interactable = true;
        if (nextWaveButton != null) nextWaveButton.interactable = true;
    }

    void GenerateSpellReward()
    {
        // cache player SpellCaster
        if (playerSpellCaster == null && GameManager.Instance.player != null)
            playerSpellCaster = GameManager.Instance.player.GetComponent<SpellCaster>();

        if (playerSpellCaster == null)
        {
            Debug.LogError("RewardScreenManager: Cannot find SpellCaster on player");
            return;
        }

        // build random spell
        var builder = new SpellBuilder();
        offeredSpell = builder.Build(playerSpellCaster);

        UpdateSpellRewardUI(offeredSpell);
    }

    void UpdateSpellRewardUI(Spell spell)
    {
        if (spell == null) return;

        // icon & name
        if (spellIcon != null && GameManager.Instance.spellIconManager != null)
            GameManager.Instance.spellIconManager.PlaceSprite(spell.IconIndex, spellIcon);

        if (spellNameText != null)
            spellNameText.text = spell.DisplayName;

        // description: modifiers first, then base spell
        if (spellDescriptionText != null && spellCatalog != null)
        {
            var lines = new List<string>();
            // collect modifier wrappers
            var cursor = spell;
            var mods = new List<ModifierSpell>();
            while (cursor is ModifierSpell m)
            {
                mods.Add(m);
                cursor = m.InnerSpell;
            }
            // for each modifier, pull its JSON description
            foreach (var m in mods)
            {
                var parts = m.DisplayName.Split(' ');
                var suffix = parts[^1]; // last word
                foreach (var kv in spellCatalog)
                {
                    var j = kv.Value;
                    if (j["name"]?.Value<string>() == suffix)
                    {
                        lines.Add($"{suffix}: {j["description"].Value<string>()}");
                        break;
                    }
                }
            }
            // then the base spell
            var baseName = cursor.DisplayName;
            foreach (var kv in spellCatalog)
            {
                var j = kv.Value;
                if (j["name"]?.Value<string>() == baseName)
                {
                    lines.Add($"{baseName}: {j["description"].Value<string>()}");
                    break;
                }
            }
            spellDescriptionText.text = string.Join("\n", lines);
        }

        // damage & mana
        if (damageValueText != null)
            damageValueText.text = Mathf.RoundToInt(spell.Damage).ToString();
        if (manaValueText != null)
            manaValueText.text = Mathf.RoundToInt(spell.Mana).ToString();
    }

    void AcceptSpell()
    {
        if (offeredSpell == null || playerSpellCaster == null)
        {
            Debug.LogWarning("Cannot accept spell: missing data");
            return;
        }

        // check for duplicate
        bool duplicate = false;
        for (int i = 0; i < playerSpellCaster.spells.Count; i++)
        {
            if (playerSpellCaster.spells[i] != null &&
                playerSpellCaster.spells[i].DisplayName == offeredSpell.DisplayName)
            {
                duplicate = true;
                Debug.Log($"Duplicate spell in slot {i}, skipping add.");
                break;
            }
        }

        if (duplicate)
        {
            OnNextWaveClicked();
            return;
        }

        // find an empty slot (max 4)
        int slot = -1;
        for (int i = 0; i < 4; i++)
        {
            if (i >= playerSpellCaster.spells.Count)
                playerSpellCaster.spells.Add(null);

            if (playerSpellCaster.spells[i] == null)
            {
                slot = i;
                break;
            }
        }

        if (slot == -1)
        {
            Debug.Log("All spell slots full; cannot add new spell");
            return;
        }

        // assign directly into the list
        playerSpellCaster.spells[slot] = offeredSpell;
        Debug.Log($"Added '{offeredSpell.DisplayName}' to slot {slot}.");

        // refresh UI & proceed
        UpdatePlayerSpellUI();
        OnNextWaveClicked();
    }

    void UpdatePlayerSpellUI()
    {
        var container = Object.FindFirstObjectByType<SpellUIContainer>();
        if (container != null)
            container.UpdateSpellUIs();
        else if (GameManager.Instance.player != null)
            GameManager.Instance.player.GetComponent<PlayerController>()?.UpdateSpellUI();
    }

    void OnNextWaveClicked()
    {
        if (rewardUI != null) rewardUI.SetActive(false);
        if (acceptSpellButton != null) acceptSpellButton.interactable = false;
        if (nextWaveButton != null) nextWaveButton.interactable = false;
        spawner?.NextWave();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Nosebleed.Pancake.Models;
using UnityEngine;

namespace DeckCurveViewer;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "com.local.vampirecrawlers.deckcurveviewer";
    public const string PluginName = "Deck Curve Viewer";
    public const string PluginVersion = "1.0.0";

    internal static ManualLogSource? LoggerInstance { get; private set; }

    public override void Load()
    {
        LoggerInstance = Log;
        AddComponent<DeckCurveOverlay>();
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}

public sealed class DeckCurveOverlay : MonoBehaviour
{
    private readonly List<CardSummary> _cards = new();
    private readonly Dictionary<int, int> _curve = new();
    private PlayerModel? _playerModel;
    private int _firstVisibleCardIndex;
    private float _nextPlayerLookup;
    private float _nextRefresh;
    private bool _showWindow;
    private string _status = "No active deck found.";

    public DeckCurveOverlay(IntPtr ptr)
        : base(ptr)
    {
    }

    private void OnGUI()
    {
        GUI.depth = -5000;

        if (Time.unscaledTime >= _nextRefresh)
        {
            RefreshDeck(forcePlayerLookup: false);
            _nextRefresh = Time.unscaledTime + 0.75f;
        }

        var scale = GetUiScale();
        var previousButtonFontSize = GUI.skin.button.fontSize;
        var previousLabelFontSize = GUI.skin.label.fontSize;
        var previousBoxFontSize = GUI.skin.box.fontSize;

        GUI.skin.button.fontSize = Mathf.RoundToInt(16f * scale);
        GUI.skin.label.fontSize = Mathf.RoundToInt(16f * scale);
        GUI.skin.box.fontSize = Mathf.RoundToInt(18f * scale);

        try
        {
            var totalCards = _cards.Sum(card => card.Count);
            if (totalCards <= 0)
            {
                _showWindow = false;
                return;
            }

            DrawCurveGraph(scale);

            var buttonLabel = $"Deck Curve ({totalCards})";
            if (GUI.Button(GetButtonRect(scale), buttonLabel))
            {
                _showWindow = !_showWindow;
                if (_showWindow)
                {
                    RefreshDeck(forcePlayerLookup: true);
                }
            }

            if (_showWindow)
            {
                DrawWindow(scale);
            }
        }
        finally
        {
            GUI.skin.button.fontSize = previousButtonFontSize;
            GUI.skin.label.fontSize = previousLabelFontSize;
            GUI.skin.box.fontSize = previousBoxFontSize;
        }
    }

    private void DrawCurveGraph(float scale)
    {
        var graphRect = GetGraphRect(scale);
        var padding = 8f * scale;
        var labelHeight = 18f * scale;
        var graphHeight = graphRect.height - padding * 2f - labelHeight;
        var bucketCount = 8;
        var bucketGap = 3f * scale;
        var bucketWidth = (graphRect.width - padding * 2f - bucketGap * (bucketCount - 1)) / bucketCount;
        var maxCount = Math.Max(1, _curve.Count == 0 ? 0 : _curve.Values.Max());

        GUI.Box(graphRect, string.Empty);

        var previousButtonFontSize = GUI.skin.button.fontSize;
        var previousFontSize = GUI.skin.label.fontSize;

        try
        {
            GUI.skin.button.fontSize = Mathf.RoundToInt(10f * scale);
            GUI.skin.label.fontSize = Mathf.RoundToInt(11f * scale);

            for (var bucket = 0; bucket < bucketCount; bucket++)
            {
                var count = GetCurveBucketCount(bucket);
                var normalizedHeight = count <= 0 ? 0f : Mathf.Max(3f * scale, graphHeight * count / maxCount);
                var x = graphRect.x + padding + bucket * (bucketWidth + bucketGap);
                var barY = graphRect.y + padding + graphHeight - normalizedHeight;
                var barRect = new Rect(x, barY, bucketWidth, normalizedHeight);

                if (count > 0)
                {
                    GUI.Button(barRect, count.ToString());
                }

                GUI.Label(
                    new Rect(x - bucketGap / 2f, graphRect.yMax - padding - labelHeight, bucketWidth + bucketGap, labelHeight),
                    bucket == 7 ? "7+" : bucket.ToString());
            }
        }
        finally
        {
            GUI.skin.button.fontSize = previousButtonFontSize;
            GUI.skin.label.fontSize = previousFontSize;
        }
    }

    private int GetCurveBucketCount(int bucket)
    {
        if (bucket < 7)
        {
            return _curve.TryGetValue(bucket, out var count) ? count : 0;
        }

        var total = 0;
        foreach (var pair in _curve)
        {
            if (pair.Key >= 7)
            {
                total += pair.Value;
            }
        }

        return total;
    }

    private void DrawWindow(float scale)
    {
        var windowRect = GetWindowRect(scale);
        var padding = 16f * scale;
        var rowHeight = 24f * scale;
        var buttonWidth = 92f * scale;

        GUI.Box(windowRect, "Deck Curve");

        var y = windowRect.y + 42f * scale;
        var contentX = windowRect.x + padding;
        var contentWidth = windowRect.width - padding * 2f;

        GUI.Label(new Rect(contentX, y, 160f * scale, rowHeight), $"Cards: {_cards.Sum(card => card.Count)}");
        if (GUI.Button(new Rect(windowRect.xMax - padding - buttonWidth * 2f - 8f * scale, y, buttonWidth, rowHeight * 1.2f), "Refresh"))
        {
            RefreshDeck(forcePlayerLookup: true);
        }

        if (GUI.Button(new Rect(windowRect.xMax - padding - buttonWidth, y, buttonWidth, rowHeight * 1.2f), "Close"))
        {
            _showWindow = false;
            return;
        }

        y += rowHeight * 1.7f;
        GUI.Label(new Rect(contentX, y, contentWidth, rowHeight), "Curve");
        y += rowHeight;

        if (_curve.Count == 0)
        {
            GUI.Label(new Rect(contentX, y, contentWidth, rowHeight), _status);
        }
        else
        {
            var x = contentX;
            foreach (var cost in _curve.Keys.OrderBy(cost => cost))
            {
                GUI.Label(new Rect(x, y, 64f * scale, rowHeight), $"{cost}: {_curve[cost]}");
                x += 66f * scale;
            }
        }

        y += rowHeight * 1.6f;
        GUI.Label(new Rect(contentX, y, contentWidth, rowHeight), "Cards");
        y += rowHeight;

        var listRect = new Rect(contentX, y, contentWidth, windowRect.yMax - padding - y - rowHeight * 1.6f);
        GUI.Box(listRect, string.Empty);

        var visibleRows = Math.Max(1, Mathf.FloorToInt((listRect.height - padding) / rowHeight));
        _firstVisibleCardIndex = Math.Max(0, Math.Min(_firstVisibleCardIndex, Math.Max(0, _cards.Count - visibleRows)));

        if (_cards.Count == 0)
        {
            GUI.Label(new Rect(listRect.x + padding, listRect.y + padding / 2f, listRect.width - padding * 2f, rowHeight), _status);
        }
        else
        {
            for (var i = 0; i < visibleRows; i++)
            {
                var cardIndex = _firstVisibleCardIndex + i;
                if (cardIndex >= _cards.Count)
                {
                    break;
                }

                var card = _cards[cardIndex];
                GUI.Label(
                    new Rect(listRect.x + padding, listRect.y + padding / 2f + i * rowHeight, listRect.width - padding * 2f, rowHeight),
                    $"{card.Cost,2}  x{card.Count,-2} {card.Name}");
            }
        }

        var navY = windowRect.yMax - padding - rowHeight * 1.2f;
        if (GUI.Button(new Rect(contentX, navY, buttonWidth, rowHeight * 1.2f), "Up"))
        {
            _firstVisibleCardIndex = Math.Max(0, _firstVisibleCardIndex - visibleRows);
        }

        if (GUI.Button(new Rect(contentX + buttonWidth + 8f * scale, navY, buttonWidth, rowHeight * 1.2f), "Down"))
        {
            _firstVisibleCardIndex = Math.Min(Math.Max(0, _cards.Count - visibleRows), _firstVisibleCardIndex + visibleRows);
        }

        GUI.Label(
            new Rect(contentX + buttonWidth * 2f + 24f * scale, navY, contentWidth - buttonWidth * 2f - 24f * scale, rowHeight),
            _cards.Count == 0 ? string.Empty : $"{_firstVisibleCardIndex + 1}-{Math.Min(_cards.Count, _firstVisibleCardIndex + visibleRows)} of {_cards.Count}");
    }

    private static float GetUiScale()
    {
        return Mathf.Clamp(Screen.height / 720f, 1.25f, 2.2f);
    }

    private static Rect GetButtonRect(float scale)
    {
        var width = 190f * scale;
        var height = 48f * scale;
        var margin = 18f * scale;
        return new Rect(margin, Screen.height - height - margin, width, height);
    }

    private static Rect GetGraphRect(float scale)
    {
        var buttonRect = GetButtonRect(scale);
        var gap = 8f * scale;
        var height = 96f * scale;
        var y = Mathf.Max(18f * scale, buttonRect.y - gap - height);
        return new Rect(buttonRect.x, y, buttonRect.width, height);
    }

    private static Rect GetWindowRect(float scale)
    {
        var margin = 24f * scale;
        var width = Mathf.Min(620f * scale, Screen.width - margin * 2f);
        var height = Mathf.Min(620f * scale, Screen.height - margin * 2f);
        return new Rect(
            (Screen.width - width) / 2f,
            (Screen.height - height) / 2f,
            width,
            height);
    }

    private void RefreshDeck(bool forcePlayerLookup)
    {
        try
        {
            var player = GetPlayerModel(forcePlayerLookup);
            if (player == null)
            {
                SetEmpty("No active player model found.");
                return;
            }

            var allCards = player.AllCards;
            if (allCards == null)
            {
                SetEmpty("Player has no AllCards collection.");
                return;
            }

            var cardCount = allCards.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<CardModel>>().Count;
            var rows = new List<CardSummary>(cardCount);
            for (var i = 0; i < cardCount; i++)
            {
                var card = allCards.get_Item(i);
                if (card == null)
                {
                    continue;
                }

                rows.Add(ReadCard(card));
            }

            _cards.Clear();
            _cards.AddRange(rows
                .GroupBy(card => new { card.Cost, Name = NormalizeCardName(card.Name) })
                .Select(group => new CardSummary(group.Key.Name, group.Key.Cost, group.Count()))
                .OrderBy(card => card.Cost)
                .ThenBy(card => card.Name, StringComparer.CurrentCultureIgnoreCase));

            _curve.Clear();
            foreach (var group in rows.GroupBy(card => card.Cost).OrderBy(group => group.Key))
            {
                _curve[group.Key] = group.Count();
            }

            _status = _cards.Count == 0 ? "Deck is empty." : string.Empty;
            _firstVisibleCardIndex = Math.Max(0, Math.Min(_firstVisibleCardIndex, Math.Max(0, _cards.Count - 1)));
        }
        catch (Exception ex)
        {
            SetEmpty("Deck read failed. See BepInEx log.");
            Plugin.LoggerInstance?.LogWarning(ex);
        }
    }

    private PlayerModel? GetPlayerModel(bool force)
    {
        if (!force && _playerModel != null && _playerModel.AllCards != null)
        {
            return _playerModel;
        }

        if (!force && Time.unscaledTime < _nextPlayerLookup)
        {
            return _playerModel;
        }

        _nextPlayerLookup = Time.unscaledTime + 2f;

        var players = Resources.FindObjectsOfTypeAll<PlayerModel>();
        _playerModel = null;

        if (players == null)
        {
            return null;
        }

        for (var i = 0; i < players.Length; i++)
        {
            var candidate = players[i];
            if (candidate == null)
            {
                continue;
            }

            var cards = candidate.AllCards;
            if (cards != null && cards.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<CardModel>>().Count > 0)
            {
                _playerModel = candidate;
                break;
            }

            // Fallback to first non-null candidate so the diagnostic surfaces even when AllCards is empty.
            _playerModel ??= candidate;
        }

        return _playerModel;
    }

    private static CardSummary ReadCard(CardModel card)
    {
        var cost = 0;
        try
        {
            cost = card.GetCardCostTypeManaCost(false);
        }
        catch
        {
            var config = card.CardConfig;
            if (config != null)
            {
                cost = config.manaCost;
            }
        }

        var name = card.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = card.CardConfig?.Name ?? "<unnamed>";
        }

        return new CardSummary(name, Math.Max(0, cost), 1);
    }

    private static string NormalizeCardName(string name)
    {
        return string.IsNullOrWhiteSpace(name) ? "<unnamed>" : name.Trim();
    }

    private void SetEmpty(string status)
    {
        _cards.Clear();
        _curve.Clear();
        _status = status;
    }

    private readonly record struct CardSummary(string Name, int Cost, int Count);
}

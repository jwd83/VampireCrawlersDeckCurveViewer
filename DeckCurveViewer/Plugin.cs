using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
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
    private const string PlayerModelTypeName = "Nosebleed.Pancake.Models.PlayerModel";
    private static readonly BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly List<CardSummary> _cards = new();
    private readonly Dictionary<int, int> _curve = new();
    private Rect _buttonRect = new(24f, 170f, 136f, 34f);
    private Rect _windowRect = new(180f, 110f, 430f, 560f);
    private Vector2 _scroll;
    private Type? _playerModelType;
    private MethodInfo? _findObjectsOfTypeAllMethod;
    private object? _playerModel;
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

        if (GUI.Button(_buttonRect, "Deck Curve"))
        {
            _showWindow = !_showWindow;
            if (_showWindow)
            {
                RefreshDeck(forcePlayerLookup: true);
            }
        }

        if (!_showWindow)
        {
            return;
        }

        if (Time.unscaledTime >= _nextRefresh)
        {
            RefreshDeck(forcePlayerLookup: false);
            _nextRefresh = Time.unscaledTime + 0.75f;
        }

        DrawWindow();
    }

    private void DrawWindow()
    {
        GUI.Box(_windowRect, "Deck Curve");

        var contentRect = new Rect(
            _windowRect.x + 12f,
            _windowRect.y + 28f,
            _windowRect.width - 24f,
            _windowRect.height - 40f);

        GUILayout.BeginArea(contentRect);
        GUILayout.BeginVertical();
        try
        {
            GUILayout.BeginHorizontal();
            try
            {
                GUILayout.Label($"Cards: {_cards.Sum(card => card.Count)}", GUILayout.Width(90f));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh", GUILayout.Width(86f)))
                {
                    RefreshDeck(forcePlayerLookup: true);
                }

                if (GUILayout.Button("Close", GUILayout.Width(70f)))
                {
                    _showWindow = false;
                }
            }
            finally
            {
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6f);
            GUILayout.Label("Curve");
            DrawCurve();

            GUILayout.Space(8f);
            GUILayout.Label("Cards");
            _scroll = GUILayout.BeginScrollView(_scroll, false, true);
            if (_cards.Count == 0)
            {
                GUILayout.Label(_status);
            }
            else
            {
                foreach (var card in _cards)
                {
                    GUILayout.Label($"{card.Cost,2}  x{card.Count,-2} {card.Name}");
                }
            }

            GUILayout.EndScrollView();
        }
        finally
        {
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
    }

    private void DrawCurve()
    {
        if (_curve.Count == 0)
        {
            GUILayout.Label(_status);
            return;
        }

        foreach (var cost in _curve.Keys.OrderBy(cost => cost))
        {
            GUILayout.Label($"{cost,2}: {_curve[cost]}");
        }
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

            var allCards = GetPropertyValue(player, "AllCards");
            var cardCount = GetCollectionCount(allCards);
            if (allCards == null || cardCount <= 0)
            {
                SetEmpty("No cards found.");
                return;
            }

            var rows = new List<CardSummary>();
            for (var i = 0; i < cardCount; i++)
            {
                var card = GetCollectionItem(allCards, i);
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

            _status = _cards.Count == 0 ? "No cards found." : string.Empty;
        }
        catch (Exception ex)
        {
            SetEmpty("Deck read failed. See BepInEx log.");
            Plugin.LoggerInstance?.LogWarning(ex);
        }
    }

    private object? GetPlayerModel(bool force)
    {
        if (!force && _playerModel != null)
        {
            return _playerModel;
        }

        if (!force && Time.unscaledTime < _nextPlayerLookup)
        {
            return _playerModel;
        }

        _nextPlayerLookup = Time.unscaledTime + 2f;
        _playerModelType ??= ResolveType(PlayerModelTypeName);
        if (_playerModelType == null)
        {
            return null;
        }

        _findObjectsOfTypeAllMethod ??= typeof(Resources)
            .GetMethods(StaticMembers)
            .FirstOrDefault(method =>
                method.Name == nameof(Resources.FindObjectsOfTypeAll)
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 0);

        if (_findObjectsOfTypeAllMethod == null)
        {
            return null;
        }

        var players = _findObjectsOfTypeAllMethod.MakeGenericMethod(_playerModelType).Invoke(null, null);
        var playerCount = GetCollectionCountOrLength(players);
        _playerModel = null;

        for (var i = 0; i < playerCount; i++)
        {
            var player = GetCollectionItem(players!, i);
            if (player != null)
            {
                _playerModel = player;
                break;
            }
        }

        return _playerModel;
    }

    private static Type? ResolveType(string fullName)
    {
        TryLoadAssembly("Assembly-CSharp");

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(fullName, throwOnError: false);
            if (type != null)
            {
                return type;
            }
        }

        Plugin.LoggerInstance?.LogWarning($"Could not resolve runtime type {fullName}");
        return null;
    }

    private static void TryLoadAssembly(string assemblyName)
    {
        try
        {
            Assembly.Load(assemblyName);
        }
        catch
        {
            // BepInEx usually loads generated interop assemblies before plugins.
        }
    }

    private static CardSummary ReadCard(object card)
    {
        var name = GetString(GetPropertyValue(card, "Name"));
        var cost = InvokeInt(card, "GetCardCostTypeManaCost", false)
            ?? InvokeInt(card, "GetManaCost")
            ?? ReadCardConfigCost(card)
            ?? 0;

        if (string.IsNullOrWhiteSpace(name))
        {
            name = ReadCardConfigName(card) ?? "<unnamed>";
        }

        return new CardSummary(name, Math.Max(0, cost), 1);
    }

    private static int? ReadCardConfigCost(object card)
    {
        var config = GetPropertyValue(card, "CardConfig");
        return config == null ? null : InvokeInt(config, "GetManaCost") ?? GetInt(GetFieldOrPropertyValue(config, "manaCost"));
    }

    private static string? ReadCardConfigName(object card)
    {
        var config = GetPropertyValue(card, "CardConfig");
        return config == null ? null : GetString(GetPropertyValue(config, "Name"));
    }

    private static object? GetPropertyValue(object? source, string propertyName)
    {
        if (source == null)
        {
            return null;
        }

        var property = source.GetType().GetProperty(propertyName, InstanceMembers | StaticMembers);
        return property?.GetValue(source);
    }

    private static object? GetFieldOrPropertyValue(object? source, string memberName)
    {
        if (source == null)
        {
            return null;
        }

        var type = source.GetType();
        var property = type.GetProperty(memberName, InstanceMembers | StaticMembers);
        if (property != null)
        {
            return property.GetValue(source);
        }

        var field = type.GetField(memberName, InstanceMembers | StaticMembers);
        return field?.GetValue(source);
    }

    private static int GetCollectionCount(object? collection)
    {
        return GetInt(GetPropertyValue(collection, "Count")) ?? 0;
    }

    private static int GetCollectionCountOrLength(object? collection)
    {
        return GetInt(GetPropertyValue(collection, "Count"))
            ?? GetInt(GetPropertyValue(collection, "Length"))
            ?? 0;
    }

    private static object? GetCollectionItem(object collection, int index)
    {
        var type = collection.GetType();
        var indexer = type.GetProperty("Item", InstanceMembers, null, null, new[] { typeof(int) }, null);
        if (indexer != null)
        {
            return indexer.GetValue(collection, new object[] { index });
        }

        var getItem = type.GetMethod("get_Item", InstanceMembers, null, new[] { typeof(int) }, null);
        return getItem?.Invoke(collection, new object[] { index });
    }

    private static int? InvokeInt(object source, string methodName, params object[] args)
    {
        var argTypes = args.Select(arg => arg.GetType()).ToArray();
        var method = source.GetType().GetMethod(methodName, InstanceMembers, null, argTypes, null)
            ?? source.GetType().GetMethods(InstanceMembers)
                .FirstOrDefault(candidate => candidate.Name == methodName && candidate.GetParameters().Length == args.Length);

        return method == null ? null : GetInt(method.Invoke(source, args));
    }

    private static int? GetInt(object? value)
    {
        if (value == null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt32(value);
        }
        catch
        {
            return null;
        }
    }

    private static string GetString(object? value)
    {
        return value?.ToString() ?? string.Empty;
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

using System;
using System.Collections.Generic;
using System.Linq;

namespace WplaceColorWatch
{

/// <summary>
/// 用户配置的颜色过滤规则。
/// “想填”和“不想填”始终互斥；后一次选择会把颜色从另一侧移走。
/// 未明确选择“想填”颜色时保持历史行为：默认允许整套内置色板，再减去排除色。
/// </summary>
public sealed class ColorRuleSet
{
    private readonly object _lock = new();
    private readonly List<BgrColor> _palette;
    private readonly HashSet<BgrColor> _wanted = new();
    private readonly HashSet<BgrColor> _excluded = new();

    public ColorRuleSet(IEnumerable<BgrColor> palette)
    {
        _palette = palette.Distinct().ToList();
        if (_palette.Count == 0)
        {
            throw new ArgumentException("颜色规则必须包含至少一种内置颜色。", nameof(palette));
        }
    }

    public int PaletteCount => _palette.Count;

    public void AddWanted(BgrColor color)
    {
        lock (_lock)
        {
            _excluded.Remove(color);
            _wanted.Add(color);
        }
    }

    public void AddExcluded(BgrColor color)
    {
        lock (_lock)
        {
            _wanted.Remove(color);
            _excluded.Add(color);
        }
    }

    public void RemoveWanted(BgrColor color)
    {
        lock (_lock)
        {
            _wanted.Remove(color);
        }
    }

    public void ClearWanted()
    {
        lock (_lock)
        {
            _wanted.Clear();
        }
    }

    public void RemoveExcluded(BgrColor color)
    {
        lock (_lock)
        {
            _excluded.Remove(color);
        }
    }

    public void SelectAllWanted()
    {
        lock (_lock)
        {
            _wanted.Clear();
            foreach (var color in _palette)
            {
                _wanted.Add(color);
            }
            _excluded.Clear();
        }
    }

    public List<BgrColor> GetWanted()
    {
        lock (_lock)
        {
            return _palette.Where(_wanted.Contains).ToList();
        }
    }

    public List<BgrColor> GetExcluded()
    {
        lock (_lock)
        {
            return _palette.Where(_excluded.Contains).ToList();
        }
    }

    public List<BgrColor> GetEffectiveColors()
    {
        lock (_lock)
        {
            var source = _wanted.Count == 0
                ? _palette
                : _palette.Where(_wanted.Contains);
            return source.Where(color => !_excluded.Contains(color)).ToList();
        }
    }

    public bool IsExplicitlyAllWanted()
    {
        lock (_lock)
        {
            return _wanted.Count == _palette.Count && _excluded.Count == 0;
        }
    }
}
}

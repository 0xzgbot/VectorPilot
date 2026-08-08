namespace VectorPilot.Engine;

/// <summary>A 3D component in the component tree (ported from Component.swift).</summary>
public sealed class Component
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; } = "Component";
    public Guid? Parent { get; set; }
    public List<Guid> Children { get; } = new();
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public double Opacity { get; set; } = 1.0;
    public string Color { get; set; } = "FFFFFF";

    public double ClampedOpacity
    {
        get => Math.Clamp(Opacity, 0.0, 1.0);
        set => Opacity = Math.Clamp(value, 0.0, 1.0);
    }
}

/// <summary>A level (layer) in the component tree (ported from Level.swift).</summary>
public sealed class Level
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; } = "Level";
    public List<Guid> Components { get; } = new();
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public double Opacity { get; set; } = 1.0;
    public string BlendMode { get; set; } = "normal";
}

/// <summary>Component tree with CRUD, hierarchy, and ordering (ported from ComponentTree.swift).</summary>
public sealed class ComponentTree
{
    private readonly Dictionary<Guid, Component> _components = new();
    private readonly Dictionary<Guid, Level> _levels = new();
    private readonly List<Guid> _rootComponents = new();

    public IReadOnlyList<Guid> RootComponents => _rootComponents;

    /// <summary>Create a component, optionally under a parent. Returns the new id.</summary>
    public Guid AddComponent(string name, Guid? parent = null)
    {
        var comp = new Component { Name = name, Parent = parent };
        _components[comp.Id] = comp;
        if (parent is { } parentId && _components.TryGetValue(parentId, out var parentComp))
        {
            parentComp.Children.Add(comp.Id);
        }
        else
        {
            _rootComponents.Add(comp.Id);
        }
        return comp.Id;
    }

    public void AddComponentToLevel(Guid componentId, Guid levelId)
    {
        if (!_components.ContainsKey(componentId)) return;
        if (_levels.TryGetValue(levelId, out var level) && !level.Components.Contains(componentId))
        {
            level.Components.Add(componentId);
        }
    }

    /// <summary>Remove a component and all descendants (from parent, levels, and the tree).</summary>
    public void RemoveComponent(Guid id)
    {
        if (!_components.TryGetValue(id, out var comp)) return;

        var allToRemove = CollectDescendants(id);
        allToRemove.Add(id);

        if (comp.Parent is { } parentId && _components.TryGetValue(parentId, out var parentComp))
        {
            parentComp.Children.RemoveAll(allToRemove.Contains);
        }
        else
        {
            _rootComponents.RemoveAll(allToRemove.Contains);
        }

        foreach (var level in _levels.Values)
        {
            level.Components.RemoveAll(allToRemove.Contains);
        }

        foreach (var uid in allToRemove)
        {
            _components.Remove(uid);
        }
    }

    public Component? GetComponent(Guid id) => _components.GetValueOrDefault(id);

    public Level? GetLevel(Guid id) => _levels.GetValueOrDefault(id);

    public Guid AddLevel(string name) { var l = new Level { Name = name }; _levels[l.Id] = l; return l.Id; }

    public void MoveComponentUp(Guid id)
    {
        int idx = SiblingIndex(id);
        if (idx <= 0) return;
        if (_components[id]?.Parent is { } parentId && _components.TryGetValue(parentId, out var parentComp))
        {
            (parentComp.Children[idx], parentComp.Children[idx - 1]) = (parentComp.Children[idx - 1], parentComp.Children[idx]);
        }
        else
        {
            (_rootComponents[idx], _rootComponents[idx - 1]) = (_rootComponents[idx - 1], _rootComponents[idx]);
        }
    }

    public void MoveComponentDown(Guid id)
    {
        int idx = SiblingIndex(id);
        if (idx < 0) return;
        if (_components[id]?.Parent is { } parentId && _components.TryGetValue(parentId, out var parentComp))
        {
            if (idx >= parentComp.Children.Count - 1) return;
            (parentComp.Children[idx], parentComp.Children[idx + 1]) = (parentComp.Children[idx + 1], parentComp.Children[idx]);
        }
        else
        {
            if (idx >= _rootComponents.Count - 1) return;
            (_rootComponents[idx], _rootComponents[idx + 1]) = (_rootComponents[idx + 1], _rootComponents[idx]);
        }
    }

    private int SiblingIndex(Guid id)
    {
        var parentId = _components.GetValueOrDefault(id)?.Parent;
        if (parentId is { } pid && _components.TryGetValue(pid, out var parentComp))
        {
            return parentComp.Children.IndexOf(id);
        }
        return _rootComponents.IndexOf(id);
    }

    private HashSet<Guid> CollectDescendants(Guid id)
    {
        var result = new HashSet<Guid>();
        if (!_components.TryGetValue(id, out var comp)) return result;
        foreach (var child in comp.Children)
        {
            result.Add(child);
            result.UnionWith(CollectDescendants(child));
        }
        return result;
    }
}

namespace Microsoft.Xna.Framework.Graphics;

/// <summary>XNA 4.0-compatible effect facade backed by one CNA effect instance.</summary>
public class Effect : GraphicsResource
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CNA.Graphics.Effect, Effect>
        FrameworkFacades = new();

    private EffectParameterCollection? _parameters;
    private EffectTechniqueCollection? _techniques;
    private EffectTechnique? _currentTechnique;

    private protected Effect(GraphicsDevice graphicsDevice, CNA.Graphics.Effect inner)
        : base(graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(inner);
        Inner = inner;
        FrameworkFacades.Add(inner, this);
    }

    public Effect(GraphicsDevice graphicsDevice, byte[] effectCode)
        : this(graphicsDevice, CNA.GameThread.Invoke(() => new CNA.Graphics.Effect(
            (graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice))).Framework,
            effectCode)))
    {
    }

    protected Effect(Effect cloneSource)
        : this(
            (cloneSource ?? throw new ArgumentNullException(nameof(cloneSource))).GraphicsDevice,
            CNA.GameThread.Invoke(() => cloneSource.Inner.Clone()))
    {
    }

    internal static Effect Adopt(GraphicsDevice graphicsDevice, CNA.Graphics.Effect inner) =>
        new(graphicsDevice, inner);

    internal static Effect? FromFramework(CNA.Graphics.Effect? inner)
    {
        if (inner is null)
        {
            return null;
        }

        return FrameworkFacades.TryGetValue(inner, out Effect? facade)
            ? facade
            : throw new InvalidOperationException(
                "A CNA effect cannot be exposed through the strict XNA facade.");
    }

    internal CNA.Graphics.Effect Inner { get; }

    public EffectParameterCollection Parameters =>
        _parameters ??= new EffectParameterCollection(Inner.Parameters);

    public EffectTechniqueCollection Techniques =>
        _techniques ??= new EffectTechniqueCollection(Inner.Techniques);

    public EffectTechnique CurrentTechnique
    {
        get
        {
            if (_currentTechnique is not null)
            {
                return _currentTechnique;
            }

            CNA.Graphics.EffectTechnique current = Inner.CurrentTechnique;
            _currentTechnique = Techniques[current.Name] ?? EffectTechnique.Wrap(current);
            return _currentTechnique;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(value, _currentTechnique))
            {
                return;
            }

            Inner.CurrentTechnique = value.Framework;
            _currentTechnique = value;
        }
    }

    public virtual Effect Clone() => new(this);

    protected internal virtual void OnApply() => Inner.Apply();

    protected override void Dispose(bool arg0)
    {
        if (IsDisposed)
        {
            return;
        }

        try
        {
            Inner?.Dispose();
        }
        finally
        {
            // XNA Effect/OcclusionQuery suppress Disposing; ABI 0.60 does the same.
            base.Dispose(false);
        }
    }
}

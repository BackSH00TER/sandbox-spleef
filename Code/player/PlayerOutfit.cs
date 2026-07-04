using Sandbox;
using System.Threading.Tasks;

/// <summary>
/// Defers a <see cref="Dresser.Apply"/> call after spawn. With Source = OwnerConnection
/// the Dresser's built-in auto-apply often runs before the owner's avatar data has
/// loaded, leaving the player naked. Runs on every client so each machine dresses
/// its local copy from the owning connection's avatar.
/// </summary>
public sealed class PlayerOutfit : Component
{
    [Property] public Dresser Dresser { get; set; }
    [Property] public float StartDelay { get; set; } = 0.05f;

    protected override void OnStart()
    {
        _ = ApplyOutfitAsync();
    }

    private async Task ApplyOutfitAsync()
    {
        Dresser dresser = Dresser ?? GetComponent<Dresser>();
        if ( dresser == null ) return;

        await Task.DelayRealtimeSeconds( StartDelay );
        if ( !this.IsValid() ) return;

        if ( this.IsBot() )
        {
            // Bots are host-owned so Dresser.Source = OwnerConnection would stream the
            // host's avatar directly, ignoring the Clothing list. Switch to Manual so
            // the outfit list is what actually gets rendered, then fan the change out
            // via Network.Refresh. Owner-only so only the host rolls / restores.
            if ( !Network.IsOwner ) return;
            dresser.Source = Dresser.ClothingSource.Manual;
            BotBrain brain = GetComponent<BotBrain>();
            await BotOutfits.ApplyForSlot( dresser, brain.IsValid() ? brain.Slot : 0 );
            if ( !this.IsValid() ) return;
            GameObject.Network.Refresh();
            return;
        }

        await dresser.Apply();
    }
}

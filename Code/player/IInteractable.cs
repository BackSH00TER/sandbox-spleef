using Sandbox;

/// <summary>
/// Implement on a Component to make its GameObject (or any of its ancestors, up to
/// the collider that got hit) interactable via <see cref="PlayerInteractor"/>. The
/// interactor traces from the local player's camera each frame, walks up the hit
/// hierarchy looking for the nearest component that implements this, paints a HUD
/// prompt, and calls <see cref="OnInteract"/> when the player presses
/// <see cref="InteractAction"/> while aiming at it.
/// </summary>
public interface IInteractable
{
    /// <summary>Input action the player must press to trigger this interactable (e.g. "Use").</summary>
    string InteractAction { get; }

    /// <summary>Label rendered next to the input glyph on the HUD prompt.</summary>
    string InteractPrompt { get; }

    /// <summary>Runs on the interacting client. <paramref name="interactor"/> is the player GameObject.</summary>
    void OnInteract( GameObject interactor );
}

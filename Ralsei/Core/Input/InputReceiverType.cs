namespace Ralsei.Core.Input;

/// <summary>How an <see cref="InputReceiver"/> answers when it is asked whether a key or a button is down.</summary>
public enum InputReceiverType
{
    /// <summary>Changes are kept in order and each question takes one: a tap over before anyone asked is still seen down, then up. For reading input as a sequence (the UI).</summary>
    Fifo,

    /// <summary>A question is answered with the state now and nothing is kept: a tap over before anyone asked is not seen. For reading input as a state (a game).</summary>
    Mailbox,
}

using System.Text;
using Eden.Mind;
using Eden.Mind.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// Root of scenes/ui/dialogue.tscn. The god talks to the selected NPC: type a line, see the reply. History is
/// kept per NPC by the Mind (<see cref="MindPool.History"/>), so switching NPCs and back shows the same
/// conversation. Requests run off the main thread; the panel polls the pending task each frame.
/// </summary>
public partial class DialoguePanel : PanelContainer
{
    private Label _title = null!;
    private RichTextLabel _log = null!;
    private LineEdit _input = null!;
    private Button _send = null!;
    private Label _status = null!;
    private readonly StringBuilder _sb = new();

    public MindPool? Mind { get; private set; }
    public int NpcId { get; private set; } = -1;
    public string NpcName { get; private set; } = "";
    public Task<DialogueReply>? Pending { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>The rendered conversation as plain text (tests, M5 check).</summary>
    public string Transcript { get; private set; } = "";

    public event Action<int, DialogueReply>? Replied;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(420, 260);
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{side}", 8);
        AddChild(margin);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        margin.AddChild(box);

        var header = new HBoxContainer();
        box.AddChild(header);
        _title = new Label { Name = "Title", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        header.AddChild(_title);
        var close = new Button { Name = "Close", Text = "x", Flat = true, FocusMode = FocusModeEnum.None };
        close.Pressed += () => ShowNpc(-1, "");
        header.AddChild(close);

        _log = new RichTextLabel
        {
            Name = "Log", BbcodeEnabled = true, ScrollFollowing = true, SelectionEnabled = true,
            SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(400, 170),
        };
        box.AddChild(_log);

        var row = new HBoxContainer();
        box.AddChild(row);
        _input = new LineEdit { Name = "Input", PlaceholderText = "Speak as the god...", SizeFlagsHorizontal = SizeFlags.ExpandFill, MaxLength = 1000 };
        _input.TextSubmitted += text => Send(text);
        row.AddChild(_input);
        _send = new Button { Name = "Send", Text = "Speak" };
        _send.Pressed += () => Send(_input.Text);
        row.AddChild(_send);

        _status = new Label { Name = "Status", Modulate = new Color(1, 1, 1, 0.7f) };
        box.AddChild(_status);
        Visible = NpcId >= 0;
        Render();
    }

    public void Bind(MindPool? mind)
    {
        Mind = mind;
        Render();
    }

    public void ShowNpc(int npcId, string name)
    {
        SpeakerId = -1; // M6: the god speaks unless ShowPossessed says otherwise
        SpeakerName = "";
        if (npcId != NpcId) { Pending = null; LastError = null; }
        NpcId = npcId;
        NpcName = name;
        Visible = npcId >= 0;
        Render();
    }

    /// <summary>Sends a line to the selected NPC (the Speak button and Enter both land here). False if nothing was sent.</summary>
    public bool Send(string text)
    {
        text = text.Trim();
        if (Mind is null || NpcId < 0 || text.Length == 0 || Pending is { IsCompleted: false }) return false;
        LastError = null;
        Pending = IsPossessedSpeaker ? Mind.AskAsPossessedAsync(NpcId, SpeakerId, SpeakerName, text) : Mind.AskAsync(NpcId, text);
        if (_input is not null) _input.Text = "";
        Render();
        return true;
    }

    public override void _Process(double delta) => Poll();

    /// <summary>Checks the pending request; called every frame.</summary>
    public void Poll()
    {
        if (Pending is not { IsCompleted: true } done) return;
        Pending = null;
        if (done.IsCompletedSuccessfully) Replied?.Invoke(NpcId, done.Result);
        else LastError = done.Exception?.InnerException is BrainException be ? $"{be.Failure}: {be.Message}" : done.Exception?.InnerException?.Message ?? "cancelled";
        Render();
    }

    public void Render()
    {
        _sb.Clear();
        var plain = new StringBuilder();
        if (Mind is not null && NpcId >= 0)
            foreach (var turn in Mind.History(NpcId))
            {
                string who = turn.Speaker switch
                {
                    Speaker.Npc => NpcName,
                    Speaker.Possessed => IsPossessedSpeaker ? $"You (as {SpeakerName})" : "You (possessing someone)",
                    _ => "You (the Voice)",
                };
                string colour = turn.Speaker == Speaker.Npc ? "#e8d7a8" : "#9cc7ff";
                _sb.Append($"[color={colour}][b]").Append(Escape(who)).Append("[/b][/color]  ").Append(Escape(turn.Text)).Append('\n');
                plain.Append(who).Append(": ").Append(turn.Text).Append('\n');
            }
        Transcript = plain.ToString();
        if (_title is null) return; // not in a tree yet
        _title.Text = NpcId < 0 ? "Speak" : IsPossessedSpeaker ? $"{SpeakerName} talks to {NpcName}" : $"Speak to {NpcName}";
        _input.PlaceholderText = IsPossessedSpeaker ? $"Speak as {SpeakerName}..." : "Speak as the god...";
        _log.Text = _sb.ToString();
        bool busy = Pending is { IsCompleted: false };
        _send.Disabled = Mind is null || busy;
        _input.Editable = Mind is not null;
        _status.Text = Mind is null ? "No mind attached."
            : busy ? $"{NpcName} is thinking..."
            : LastError is not null ? LastError
            : Mind.Status.State == MindState.Off ? "Brain is off (settings): they hear you but cannot answer." : "";
    }

    private static string Escape(string s) => s.Replace("[", "[lb]");

    // ---- M6: the possessed NPC as speaker ---------------------------------------------------------

    /// <summary>Who speaks: -1 for the god, else the possessed NPC's id (see <see cref="ShowPossessed"/>).</summary>
    public int SpeakerId { get; private set; } = -1;
    public string SpeakerName { get; private set; } = "";
    public bool IsPossessedSpeaker => SpeakerId >= 0;

    /// <summary>M6: opens the panel for <paramref name="npcId"/> with the possessed NPC speaking face to face.</summary>
    public void ShowPossessed(int npcId, string name, int speakerId, string speakerName)
    {
        ShowNpc(npcId, name);
        SpeakerId = speakerId;
        SpeakerName = speakerName;
        Render();
    }

    /// <summary>Puts the keyboard focus in the input line.</summary>
    public void FocusInput() => _input?.CallDeferred(Control.MethodName.GrabFocus);
}

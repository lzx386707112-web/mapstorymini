using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

public static class CurrentFeedbackChecks
{
    const BindingFlags Private = BindingFlags.Static | BindingFlags.NonPublic;
    static void Require(bool value, string label) { if (!value) throw new Exception(label); }
    static void Set(string field, object value) => typeof(SkillWindow).GetField(field, Private).SetValue(null, value);
    static object Call(string method, params object[] args) => typeof(SkillWindow).GetMethod(method, Private).Invoke(null, args);

    public static void Run()
    {
        var oldProfile = typeof(Profile).GetField("data", Private).GetValue(null);
        var oldOverride = typeof(Profile).GetField("debugOverride", Private).GetValue(null);
        var oldEvent = Event.current;
        var oldPointer = Pointer.Pos;
        bool oldPressed = Pointer.Pressed, oldDown = Pointer.Down;
        try
        {
            typeof(Profile).GetField("data", Private).SetValue(null, new PlayerProfileData());
            typeof(Profile).GetField("debugOverride", Private).SetValue(null, true);
            // Constructing player slots must not enumerate any VFX.
            var clips = (System.Collections.IDictionary)typeof(NativeSkillVfx).GetField("skills", Private).GetValue(null);
            int before = clips.Count;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++)
                _ = new SkillDef(new ClassSkill { art = "1221009", name = "" }, SkillBook.Native(1221009, 1));
            Require(clips.Count == before, "login slot construction eagerly loaded VFX");
            Debug.Log("CURRENT_FEEDBACK_PASS lazy_1000_slot_ms=" + watch.ElapsedMilliseconds);

            CheckPanels();
        }
        finally
        {
            SkillWindow.CancelSkillDrag();
            Pointer.Pos = oldPointer; Pointer.Pressed = oldPressed; Pointer.Down = oldDown; Event.current = oldEvent;
            typeof(Profile).GetField("data", Private).SetValue(null, oldProfile);
            typeof(Profile).GetField("debugOverride", Private).SetValue(null, oldOverride);
        }
    }

    public static void CheckScrollbar()
    {
        Event.current = new Event { type = EventType.Repaint };
        var ids = new List<int>();
        foreach (var tab in SkillBook.Tabs(SkillBook.TreeForJob(122)))
            foreach (var raw in (List<object>)tab["skills"])
                ids.Add(Convert.ToInt32(raw));
        Profile.Data.SetJob(122);
        foreach (int id in ids) Profile.Data.SetSkillLevel(id, 1);
        var win = new Rect(0, 0, 175 * BottomBar.S, 289 * BottomBar.S);
        Set("scroll", 0);
        var next = (Rect)Call("ScrollNextRect", win);
        for (int i = 0; i < ids.Count + 2; i++)
        {
            Pointer.Pos = next.center; Pointer.Pressed = Pointer.Down = true;
            Call("HandleScrollbar", win, ids);
            Call("HandleRows", win, ids);
            Require(!SkillWindow.Dragging, "scroll arrow picked a skill");
            Require((int)Call("RowAt", win, ids) == -1, "scrollbar overlaps a skill row");
        }
        Require((int)typeof(SkillWindow).GetField("scroll", Private).GetValue(null) == ids.Count - 4,
            "last skill row is unreachable");
        Debug.Log("CURRENT_FEEDBACK_PASS paladin_all_rows=" + ids.Count + " scroll_without_pickup");
    }

    static void CheckPanels()
    {
        bool keyboard = KeyConfigWindow.Visible, skill = SkillWindow.Visible, inventory = InvenWindow.Visible;
        var elements = VirtualControls.elems;
        try
        {
            if (!keyboard) KeyConfigWindow.Toggle();
            SkillWindow.Visible = InvenWindow.Visible = false;
            NativeKeyActions.Execute(GameAction.Skill); NativeKeyActions.Execute(GameAction.Inven);
            Require(KeyConfigWindow.Visible && SkillWindow.Visible && InvenWindow.Visible, "keyboard closes peer panels");
            VirtualControls.Ensure();
            elements = VirtualControls.elems;
            var button = new VCElem { type = VCElemType.Button, key = KeyCode.None, action = GameAction.Skill,
                cx = .5f, cy = .5f, size = .1f };
            VirtualControls.elems = new List<VCElem> { button };
            var hit = typeof(VirtualControls).GetMethod("HitElement", Private);
            Require(ReferenceEquals(hit.Invoke(null, new object[] { button.Rect.center }), button), "keyboard blocks virtual panel key");
            button.action = GameAction.Attack;
            Require(hit.Invoke(null, new object[] { button.Rect.center }) == null, "keyboard edit leaked a gameplay key");
            Debug.Log("CURRENT_FEEDBACK_PASS keyboard_peer_panels virtual_panel_button");
        }
        finally
        {
            VirtualControls.elems = elements;
            if (!keyboard && KeyConfigWindow.Visible) KeyConfigWindow.Toggle();
            SkillWindow.Visible = skill; InvenWindow.Visible = inventory;
        }
    }
}

using System.Collections.Generic;
using RoadToValor.Core;
using UnityEngine;

namespace RoadToValor.Game
{
    /// <summary>
    /// RTS mouse/keyboard command layer:
    /// left click select (Shift add, double-click same type), drag box select, right click move / attack / clear rubble,
    /// A + click attack-move, S stop, H hold, F ability, Ctrl+1..9 save group, 1..9 recall (double tap centres camera),
    /// Space centre on selection, F1..F8 / card click to deploy (left click place, right click / Esc cancel).
    /// Every action becomes a GameCommand for the simulation.
    /// </summary>
    public sealed class CommandController
    {
        public readonly List<int> Selection = new List<int>(64);
        private readonly List<int>[] _groups = new List<int>[10];
        private readonly BattleSimulation _sim;
        private readonly RtsCamera _cam;
        private readonly UnitViewManager _views;
        private readonly FxManager _fx;
        private readonly Team _team;

        public System.Func<Vector2, bool> IsOverUi = p => false;
        public bool AttackMoveArmed;
        public int PlacingDefId = -1;
        public bool PlacementValid;
        public string PlacementReason = "";
        public Vector3 PlacementPoint;
        public int PlacementPadId = -1;
        public bool Dragging;
        public Vector2 DragStart;
        public Vector2 DragEnd;
        public UnitView Hovered;
        public string Feedback = "";
        public int LastEscapeFrame = -1;
        public float FeedbackTime;

        private float _lastClickTime;
        private int _lastClickDefId = -1;
        private float _lastGroupTime;
        private int _lastGroup = -1;
        private float _aHeld, _sHeld;
        private bool _leftDownInWorld;

        public CommandController(BattleSimulation sim, RtsCamera cam, UnitViewManager views, FxManager fx, Team team)
        {
            _sim = sim;
            _cam = cam;
            _views = views;
            _fx = fx;
            _team = team;
            for (int i = 0; i < 10; i++) _groups[i] = new List<int>();
            cam.KeyFilter = CameraKeyFilter;
        }

        private bool CameraKeyFilter(GameKey k)
        {
            // When units are selected, A and S are commands: only pan if they are held deliberately.
            if (Selection.Count == 0) return true;
            if (k == GameKey.A) return _aHeld > 0.25f;
            if (k == GameKey.S) return _sHeld > 0.25f;
            return true;
        }

        public void Say(string text)
        {
            Feedback = text;
            FeedbackTime = Time.unscaledTime;
        }

        public List<SimUnit> SelectedUnits()
        {
            var list = new List<SimUnit>(Selection.Count);
            for (int i = Selection.Count - 1; i >= 0; i--)
            {
                var u = _sim.Get(Selection[i]);
                if (u == null || !u.Alive) { Selection.RemoveAt(i); continue; }
                list.Add(u);
            }
            list.Reverse();
            return list;
        }

        public void ClearSelection()
        {
            foreach (int id in Selection) { var v = _views.Get(id); if (v != null) v.SetSelected(false, _team); }
            Selection.Clear();
        }

        public void Select(IEnumerable<int> ids, bool add)
        {
            if (!add) ClearSelection();
            foreach (int id in ids)
            {
                if (Selection.Contains(id)) continue;
                var u = _sim.Get(id);
                if (u == null || !u.Alive || u.Team != _team) continue;
                Selection.Add(id);
                var v = _views.Get(id);
                if (v != null) v.SetSelected(true, _team);
            }
        }

        private void RefreshRings()
        {
            foreach (int id in Selection) { var v = _views.Get(id); if (v != null && !v.Selected) v.SetSelected(true, _team); }
        }

        public void BeginPlacement(int defId)
        {
            var def = _sim.Units.Get(defId);
            if (def == null) return;
            if (!_sim.CanDeploy(_team, defId, _sim.Map.Layout.GetHq(_team).Position, -1, out string reason) && (reason.StartsWith("CP") || reason.StartsWith("재사용")))
            {
                Say(def.NameKo + ": " + reason);
                return;
            }
            PlacingDefId = defId;
            AttackMoveArmed = false;
        }

        public void CancelPlacement() { PlacingDefId = -1; }

        private UnitView PickUnit(Vector2 mouse, bool preferEnemy, out float bestDist)
        {
            UnitView best = null;
            bestDist = float.MaxValue;
            var cam = _cam.Cam;
            foreach (var v in _views.Active)
            {
                if (!v.OnScreen || v.Unit == null || !v.Unit.Alive) continue;
                var u = v.Unit;
                if (u.Team != _team && u.Def.Ability == AbilityType.Stealth && !u.Revealed) continue;
                Vector3 sp = cam.WorldToScreenPoint(v.WorldPos + Vector3.up * v.Height * 0.45f);
                if (sp.z <= 0f) continue;
                Vector3 sp2 = cam.WorldToScreenPoint(v.WorldPos + cam.transform.right * Mathf.Max(1f, u.Radius * v.VisualScale));
                float pxRadius = Mathf.Max(14f, Mathf.Abs(sp2.x - sp.x) * 1.4f);
                float d = Vector2.Distance(mouse, new Vector2(sp.x, sp.y));
                if (d > pxRadius) continue;
                float score = d - (preferEnemy == (u.Team != _team) ? 1000f : 0f);
                if (score < bestDist) { bestDist = score; best = v; }
            }
            return best;
        }

        public void Update(float dt)
        {
            if (Selection.Count > 0) RefreshRings();
            _aHeld = GameInput.Held(GameKey.A) ? _aHeld + dt : 0f;
            _sHeld = GameInput.Held(GameKey.S) ? _sHeld + dt : 0f;

            Vector2 mouse = GameInput.MousePosition;
            bool overUi = IsOverUi(mouse);
            Hovered = overUi ? null : PickUnit(mouse, true, out _);

            HandleKeys();

            // --- card placement mode
            if (PlacingDefId >= 0)
            {
                var def = _sim.Units.Get(PlacingDefId);
                PlacementPadId = -1;
                if (_cam.ScreenToGround(mouse, out Vector3 gp))
                {
                    PlacementPoint = gp;
                    if (def.IsStructure)
                    {
                        var pad = _sim.PadNear(gp.ToVec2(), 16f);
                        if (pad != null) { PlacementPadId = pad.Id; PlacementPoint = pad.Pos.ToV3(_sim.Map.SampleHeight(pad.Pos)); }
                    }
                    PlacementValid = _sim.CanDeploy(_team, PlacingDefId, PlacementPoint.ToVec2(), PlacementPadId, out PlacementReason);
                }
                else { PlacementValid = false; PlacementReason = "지면을 가리키세요"; }

                if (!overUi && GameInput.MouseDown(0))
                {
                    if (PlacementValid)
                    {
                        _sim.Enqueue(GameCommand.Deploy(_team, PlacingDefId, PlacementPoint.ToVec2(), PlacementPadId));
                        _fx.CommandMarker(PlacementPoint, false);
                        if (!GameInput.Shift) PlacingDefId = -1;
                    }
                    else Say(def.NameKo + ": " + PlacementReason);
                }
                if (GameInput.KeyDown(GameKey.Escape)) LastEscapeFrame = Time.frameCount;
                if (GameInput.MouseDown(1) || GameInput.KeyDown(GameKey.Escape)) PlacingDefId = -1;
                return;
            }

            // --- left mouse: select / box select / attack-move
            if (GameInput.MouseDown(0) && !overUi)
            {
                if (AttackMoveArmed)
                {
                    AttackMoveArmed = false;
                    var target = PickUnit(mouse, true, out _);
                    if (target != null && target.Unit.Team != _team) IssueAttack(target.Unit);
                    else if (_cam.ScreenToGround(mouse, out Vector3 gp)) IssueUnits(CommandType.AttackMove, gp);
                    return;
                }
                _leftDownInWorld = true;
                Dragging = false;
                DragStart = mouse;
            }
            if (_leftDownInWorld && GameInput.MouseHeld(0))
            {
                DragEnd = mouse;
                if (!Dragging && Vector2.Distance(DragStart, DragEnd) > 8f) Dragging = true;
            }
            if (_leftDownInWorld && GameInput.MouseUp(0))
            {
                _leftDownInWorld = false;
                if (Dragging) BoxSelect(DragStart, mouse, GameInput.Shift);
                else ClickSelect(mouse);
                Dragging = false;
            }

            // --- right mouse: contextual command
            if (GameInput.MouseDown(1) && !overUi)
            {
                AttackMoveArmed = false;
                if (Selection.Count == 0) return;
                var target = PickUnit(mouse, true, out _);
                if (target != null && target.Unit.Team != _team && target.Unit.Team != Team.Neutral)
                {
                    IssueAttack(target.Unit);
                    return;
                }
                if (_cam.ScreenToGround(mouse, out Vector3 gp))
                {
                    var pad = _sim.PadNear(gp.ToVec2(), 7f);
                    if (pad != null && pad.State == PadState.Rubble && HasEngineers())
                    {
                        var ids = new List<int>();
                        foreach (var u in SelectedUnits()) if (u.Def.Ability == AbilityType.Repair) ids.Add(u.Id);
                        _sim.Enqueue(new GameCommand { Type = CommandType.ClearRubble, Team = _team, UnitIds = ids.ToArray(), PadId = pad.Id, Target = pad.Pos });
                        _fx.CommandMarker(pad.Pos.ToV3(gp.y), false);
                        Say("공병: 잔해 철거 명령");
                        return;
                    }
                    IssueUnits(CommandType.Move, gp);
                }
            }
        }

        private bool HasEngineers()
        {
            foreach (var u in SelectedUnits()) if (u.Def.Ability == AbilityType.Repair) return true;
            return false;
        }

        private void HandleKeys()
        {
            if (GameInput.KeyDown(GameKey.Escape))
            {
                if (AttackMoveArmed) { AttackMoveArmed = false; LastEscapeFrame = Time.frameCount; }
            }
            for (int i = 0; i < GameInput.CardKeys.Length; i++)
            {
                if (!GameInput.KeyDown(GameInput.CardKeys[i])) continue;
                var deck = _sim.TeamOf(_team).Deck;
                if (i < deck.Length) BeginPlacement(deck[i]);
            }
            if (PlacingDefId >= 0) return;

            if (Selection.Count > 0)
            {
                if (GameInput.KeyDown(GameKey.A)) { AttackMoveArmed = true; Say("공격 이동: 목표를 클릭"); }
                if (GameInput.KeyDown(GameKey.S)) { IssueSimple(CommandType.Stop); Say("정지"); }
                if (GameInput.KeyDown(GameKey.H)) { IssueSimple(CommandType.Hold); Say("위치 사수"); }
                if (GameInput.KeyDown(GameKey.F)) UseAbility();
                if (GameInput.KeyDown(GameKey.Space)) CenterOnSelection();
            }

            for (int d = 0; d <= 9; d++)
            {
                if (!GameInput.KeyDown(GameInput.Digits[d])) continue;
                if (GameInput.Ctrl)
                {
                    _groups[d].Clear();
                    _groups[d].AddRange(Selection);
                    Say("부대 " + d + " 저장 (" + Selection.Count + ")");
                }
                else
                {
                    var alive = new List<int>();
                    foreach (int id in _groups[d]) { var u = _sim.Get(id); if (u != null && u.Alive) alive.Add(id); }
                    _groups[d].Clear();
                    _groups[d].AddRange(alive);
                    if (alive.Count == 0) continue;
                    Select(alive, GameInput.Shift);
                    if (_lastGroup == d && Time.unscaledTime - _lastGroupTime < 0.35f) CenterOnSelection();
                    _lastGroup = d;
                    _lastGroupTime = Time.unscaledTime;
                }
            }
        }

        public void UseAbility()
        {
            var ids = new List<int>();
            foreach (var u in SelectedUnits()) if (BattleSimulation.HasActiveAbility(u.Def)) ids.Add(u.Id);
            if (ids.Count == 0) { Say("사용 가능한 특수 능력 없음"); return; }
            _sim.Enqueue(new GameCommand { Type = CommandType.UseAbility, Team = _team, UnitIds = ids.ToArray(), PadId = -1 });
            Say("특수 능력 사용");
        }

        public void CenterOnSelection()
        {
            var units = SelectedUnits();
            if (units.Count == 0) return;
            Vec2 c = Vec2.Zero;
            foreach (var u in units) c += u.Pos;
            c /= units.Count;
            _cam.SetFocus(c.ToV3(_sim.Map.SampleHeight(c)));
        }

        private void ClickSelect(Vector2 mouse)
        {
            var v = PickUnit(mouse, false, out _);
            if (v == null || v.Unit.Team != _team)
            {
                if (!GameInput.Shift) ClearSelection();
                return;
            }
            bool dbl = Time.unscaledTime - _lastClickTime < 0.32f && _lastClickDefId == v.Unit.Def.Id;
            _lastClickTime = Time.unscaledTime;
            _lastClickDefId = v.Unit.Def.Id;
            if (dbl)
            {
                // double-click: all units of this type on screen
                var ids = new List<int>();
                foreach (var o in _views.Active)
                    if (o.OnScreen && o.Unit != null && o.Unit.Alive && o.Unit.Team == _team && o.Unit.Def.Id == v.Unit.Def.Id) ids.Add(o.Unit.Id);
                Select(ids, GameInput.Shift);
                return;
            }
            if (GameInput.Shift && Selection.Contains(v.Unit.Id))
            {
                Selection.Remove(v.Unit.Id);
                v.SetSelected(false, _team);
                return;
            }
            Select(new[] { v.Unit.Id }, GameInput.Shift);
        }

        private void BoxSelect(Vector2 a, Vector2 b, bool add)
        {
            var rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            var mobile = new List<int>();
            var structures = new List<int>();
            var cam = _cam.Cam;
            foreach (var v in _views.Active)
            {
                if (!v.OnScreen || v.Unit == null || !v.Unit.Alive || v.Unit.Team != _team) continue;
                Vector3 sp = cam.WorldToScreenPoint(v.WorldPos + Vector3.up * v.Height * 0.4f);
                if (sp.z <= 0f || !rect.Contains(new Vector2(sp.x, sp.y))) continue;
                if (v.Unit.IsStructure) structures.Add(v.Unit.Id); else mobile.Add(v.Unit.Id);
            }
            Select(mobile.Count > 0 ? mobile : structures, add);
        }

        private void IssueSimple(CommandType t)
        {
            var ids = new List<int>();
            foreach (var u in SelectedUnits()) if (!u.IsStructure) ids.Add(u.Id);
            if (ids.Count == 0) return;
            _sim.Enqueue(GameCommand.Units(t, _team, ids.ToArray(), Vec2.Zero));
        }

        private void IssueUnits(CommandType t, Vector3 ground)
        {
            var ids = new List<int>();
            foreach (var u in SelectedUnits()) if (!u.IsStructure) ids.Add(u.Id);
            if (ids.Count == 0) return;
            _sim.Enqueue(GameCommand.Units(t, _team, ids.ToArray(), ground.ToVec2()));
            _fx.CommandMarker(ground, t == CommandType.AttackMove);
        }

        private void IssueAttack(SimUnit target)
        {
            var ids = new List<int>();
            foreach (var u in SelectedUnits()) if (!u.IsStructure && u.Def.CanAttack) ids.Add(u.Id);
            if (ids.Count == 0) { Say("공격 가능한 유닛이 없습니다"); return; }
            _sim.Enqueue(GameCommand.Units(CommandType.AttackUnit, _team, ids.ToArray(), target.Pos, target.Id));
            _fx.CommandMarker(target.Pos.ToV3(_sim.Map.GroundHeight(target.Pos)), true);
        }
    }
}

using System.Collections.Generic;
using Studio;
using UnityEngine;

namespace GinPadHack
{
    // Studio's selection as item mode sees it (PadWindow.Items.cs). Item mode is on while the
    // selection holds anything that is not a character: an item, a folder, a light ... With several
    // objects selected the Move tab moves all of them, characters included.
    internal sealed class ItemSelection
    {
        const float ShapesRecheckSeconds = 1f;   // KKPE finds an item's Blend Shapes a moment after it is added

        static ItemSelection current;
        static int currentFrame = -1;

        internal readonly List<ObjectCtrlInfo> Objects = new List<ObjectCtrlInfo>();   // in Studio's order
        internal readonly List<ObjectCtrlInfo> Roots = new List<ObjectCtrlInfo>();     // without objects whose parent is selected too
        internal readonly bool[] TabsUsable = { true, false };                          // Move, Shapes
        internal PadTarget Shapes;     // KKPE's Blend Shape editor of the first selected item that has Blend Shapes
        internal string Caption = "";
        float shapesRecheckAt;

        // Look at Studio's selection again even within this frame (the isolated tests change it).
        internal static void Forget()
        {
            currentFrame = -1;
        }

        // The selection, or null while nothing or only characters are selected.
        internal static ItemSelection Current()
        {
            if (currentFrame == Time.frameCount)
            {
                return current;
            }

            currentFrame = Time.frameCount;
            ObjectCtrlInfo[] selected = Singleton<Studio.Studio>.IsInstance() ? Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectObjectCtrl : null;
            if (selected == null || selected.Length == 0 || AllCharacters(selected))
            {
                current = null;
            }
            else if (current == null || !current.Holds(selected))
            {
                current = new ItemSelection(selected);
            }

            if (current != null)
            {
                current.FindShapes();
            }

            return current;
        }

        static bool AllCharacters(ObjectCtrlInfo[] selected)
        {
            foreach (ObjectCtrlInfo info in selected)
            {
                if (!(info is OCIChar))
                {
                    return false;
                }
            }

            return true;
        }

        ItemSelection(ObjectCtrlInfo[] selected)
        {
            foreach (ObjectCtrlInfo info in selected)
            {
                if (info != null && info.guideObject != null)
                {
                    Objects.Add(info);
                }
            }

            foreach (ObjectCtrlInfo info in Objects)
            {
                if (!HasSelectedParent(info))
                {
                    Roots.Add(info);
                }
            }

            if (Objects.Count > 0)
            {
                string name = Objects[0].treeNodeObject == null ? "" : Objects[0].treeNodeObject.textName;
                Caption = name + (Objects.Count > 1 ? " +" + (Objects.Count - 1) : "");
            }
        }

        bool Holds(ObjectCtrlInfo[] selected)
        {
            if (selected.Length != Objects.Count)
            {
                return false;
            }

            for (int i = 0; i < selected.Length; i++)
            {
                if (selected[i] != Objects[i])
                {
                    return false;
                }
            }

            return true;
        }

        // Moving a parent already moves its children; moving them as well would move them twice.
        bool HasSelectedParent(ObjectCtrlInfo info)
        {
            for (ObjectCtrlInfo parent = info.parentInfo; parent != null; parent = parent.parentInfo)
            {
                if (Objects.Contains(parent))
                {
                    return true;
                }
            }

            return false;
        }

        void FindShapes()
        {
            if (Shapes != null || Time.realtimeSinceStartup < shapesRecheckAt)
            {
                return;
            }

            shapesRecheckAt = Time.realtimeSinceStartup + ShapesRecheckSeconds;
            foreach (ObjectCtrlInfo info in Objects)
            {
                PadTarget target = info is OCIItem ? Kkpe.ItemTarget(info) : null;
                if (target != null && Kkpe.Meshes(target).Count > 0)
                {
                    Shapes = target;
                    TabsUsable[1] = true;
                    return;
                }
            }
        }

        // Every object that can be animated: characters, and items with a running Animator.
        internal List<Animator> Animators()
        {
            List<Animator> animators = new List<Animator>();
            foreach (ObjectCtrlInfo info in Objects)
            {
                OCIChar character = info as OCIChar;
                OCIItem item = info as OCIItem;
                Animator animator = character != null && character.charAnimeCtrl != null ? character.charAnimeCtrl.animator
                    : item != null && item.isAnime ? item.animator
                    : null;
                if (animator != null)
                {
                    animators.Add(animator);
                }
            }

            return animators;
        }
    }

    // Stepping through an FK chain with the mouse wheel, as Move Controller does.
    //
    // The chain is the FK node list of the object whose FK node is selected in Studio. "active" is
    // that node. Shift + wheel grows or shrinks a range of nodes around it (shown lit, turned
    // together); the plain wheel moves the range, or the single node, along the chain.
    internal sealed class FkWalk
    {
        List<OCIChar.BoneInfo> chain;
        GuideObject selected;          // the node this walk last saw selected
        int active = -1;
        int first = -1, last = -1;     // the range; -1 while there is none

        internal bool HasBone
        {
            get
            {
                return chain != null && active >= 0;
            }
        }

        internal string ActiveName
        {
            get
            {
                return HasBone ? chain[active].guideObject.transformTarget.name : "";
            }
        }

        internal int Count
        {
            get
            {
                return !HasBone ? 0 : first < 0 ? 1 : last - first + 1;
            }
        }

        // Follow Studio's selected guide object. Another selected node starts a new walk.
        internal void Sync(List<ObjectCtrlInfo> objects)
        {
            GuideObject now = Singleton<GuideObjectManager>.IsInstance() ? Singleton<GuideObjectManager>.Instance.selectObject : null;
            if (now == selected && (now == null || HasBone))
            {
                return;
            }

            ClearRange();
            selected = now;
            chain = null;
            active = -1;
            if (now == null)
            {
                return;
            }

            foreach (ObjectCtrlInfo info in objects)
            {
                List<OCIChar.BoneInfo> bones = FkChain(info);
                int index = bones == null ? -1 : bones.FindIndex(delegate (OCIChar.BoneInfo bone) { return bone.guideObject == now; });
                if (index >= 0)
                {
                    chain = bones;
                    active = index;
                    return;
                }
            }
        }

        // The FK node list of an item with FK, or of a character with FK on; otherwise null.
        static List<OCIChar.BoneInfo> FkChain(ObjectCtrlInfo info)
        {
            OCIItem item = info as OCIItem;
            if (item != null)
            {
                return item.isFK && item.itemFKCtrl != null && item.itemFKCtrl.enabled ? item.listBones : null;
            }

            OCIChar character = info as OCIChar;
            return character != null && character.fkCtrl != null && character.fkCtrl.enabled ? character.listBones : null;
        }

        // The nodes the coloured turn buttons and Reset FK act on.
        internal List<OCIChar.BoneInfo> Bones()
        {
            List<OCIChar.BoneInfo> bones = new List<OCIChar.BoneInfo>();
            if (!HasBone)
            {
                return bones;
            }

            if (first < 0)
            {
                bones.Add(chain[active]);
            }
            else
            {
                bones.AddRange(chain.GetRange(first, last - first + 1));
            }

            return bones;
        }

        // One wheel notch: +1 towards the end of the chain, -1 towards its start.
        //   range: grow the range away from the active node, or shrink it back towards it
        //   single: select the neighbouring node alone
        //   neither: move the range (or the single node) one node along the chain
        internal void Step(int direction, bool range, bool single)
        {
            if (!HasBone)
            {
                return;
            }

            if (range)
            {
                StepRange(direction);
            }
            else if (single || first < 0)
            {
                SelectNeighbour(direction);
            }
            else
            {
                SlideRange(direction);
            }
        }

        void SelectNeighbour(int direction)
        {
            if (first >= 0)
            {
                // Continue from the end of the range that faces the step.
                active = direction > 0 ? last : first;
            }

            int next = active + direction;
            if (next < 0 || next >= chain.Count)
            {
                return;
            }

            ClearRange();
            active = next;
            selected = chain[active].guideObject;
            Singleton<GuideObjectManager>.Instance.selectObject = selected;
        }

        void StepRange(int direction)
        {
            if (first < 0)
            {
                first = last = active;
            }

            if (direction > 0)
            {
                if (first < active)
                {
                    Light(first++, false);           // shrink back towards the active node
                }
                else if (last < chain.Count - 1)
                {
                    Light(++last, true);             // grow towards the end
                }
            }
            else
            {
                if (last > active)
                {
                    Light(last--, false);
                }
                else if (first > 0)
                {
                    Light(--first, true);
                }
            }
        }

        void SlideRange(int direction)
        {
            if (direction > 0 && last < chain.Count - 1)
            {
                Light(++last, true);
                Light(first++, false);
                active = first;
            }
            else if (direction < 0 && first > 0)
            {
                Light(--first, true);
                Light(last--, false);
                active = last;
            }
        }

        void Light(int index, bool on)
        {
            chain[index].guideObject.isActive = on;
        }

        void ClearRange()
        {
            if (chain != null && first >= 0)
            {
                for (int i = first; i <= last && i < chain.Count; i++)
                {
                    chain[i].guideObject.isActive = i == active;
                }
            }

            first = last = -1;
        }
    }

    // One step of Studio's Undo for the positions and rotations an edit changed.
    internal sealed class TransformsCommand : ICommand
    {
        sealed class Change
        {
            internal int Key;
            internal Vector3 OldPosition, NewPosition, OldRotation, NewRotation;
        }

        readonly List<Change> changes = new List<Change>();

        internal int Count
        {
            get
            {
                return changes.Count;
            }
        }

        internal void Add(int key, Vector3 oldPosition, Vector3 newPosition, Vector3 oldRotation, Vector3 newRotation)
        {
            Change change = new Change();
            change.Key = key;
            change.OldPosition = oldPosition;
            change.NewPosition = newPosition;
            change.OldRotation = oldRotation;
            change.NewRotation = newRotation;
            changes.Add(change);
        }

        public void Do()
        {
            Apply(true);
        }

        public void Redo()
        {
            Apply(true);
        }

        public void Undo()
        {
            Apply(false);
        }

        void Apply(bool newValues)
        {
            foreach (Change change in changes)
            {
                ChangeAmount amount = Studio.Studio.GetChangeAmount(change.Key);
                if (amount != null)
                {
                    amount.pos = newValues ? change.NewPosition : change.OldPosition;
                    amount.rot = newValues ? change.NewRotation : change.OldRotation;
                }
            }
        }
    }
}

using System.Collections.Generic;
using Studio;
using UnityEngine;

namespace GinPadHack
{
    // Studio's Undo (Ctrl + Z) and Redo (Shift + Z) for the Body tab.
    //
    // KKPE keeps no Undo for its bone overrides and none for its limb copies, so the Body tab
    // takes a snapshot of the character's pose when a press begins and turns the difference into
    // one step of Studio's Undo once the edit is over. The pose is everything the Body tab (and
    // KKPE's limb functions) can change:
    //   the character's root  position and rotation (the Body tab's Root trapezoid)
    //   Studio's IK targets   position and rotation
    //   Studio's FK nodes     rotation
    //   KKPE's bone overrides position, rotation and scale; a missing override counts too, so
    //                         undoing "Reset bone" brings the override back
    //
    // KKPE pushes a step of its own for Swap pose and the limb copies (IK targets and FK nodes
    // only). The Body tab's step holds all of that as well, so KKPE's step is taken off again:
    // one Ctrl + Z undoes one press.

    // KKPE's overrides of one bone; a part is null when KKPE leaves it to the game.
    internal sealed class BoneOverride
    {
        internal static readonly BoneOverride None = new BoneOverride();

        internal Vector3? Position, Scale;
        internal Quaternion? Rotation;

        internal bool Same(BoneOverride other)
        {
            return Position == other.Position && Rotation == other.Rotation && Scale == other.Scale;
        }
    }

    internal sealed class PoseSnapshot
    {
        internal readonly PadTarget Target;
        internal readonly int UndoDepth;   // steps on Studio's Undo stack when the snapshot was taken
        internal readonly Dictionary<int, Vector3[]> Guides = new Dictionary<int, Vector3[]>();   // dicKey -> position, rotation
        internal readonly Dictionary<Transform, BoneOverride> Bones;

        internal PoseSnapshot(PadTarget target)
        {
            Target = target;
            UndoDepth = UndoStack.Depth();
            Remember(target.Character.guideObject);
            foreach (OCIChar.IKInfo ik in target.Character.listIKTarget)
            {
                Remember(ik.guideObject);
            }

            foreach (OCIChar.BoneInfo fk in target.Character.listBones)
            {
                Remember(fk.guideObject);
            }

            Bones = Kkpe.Overrides(target);
        }

        void Remember(GuideObject guide)
        {
            if (guide != null)
            {
                Guides[guide.dicKey] = new Vector3[] { guide.changeAmount.pos, guide.changeAmount.rot };
            }
        }
    }

    // Studio's Undo stack (UndoRedoManager keeps it private).
    internal static class UndoStack
    {
        const string KkpeStep = "HSPE.Commands.MoveRotateEqualsCommand";

        static Stack<ICommand> Steps()
        {
            return Singleton<UndoRedoManager>.IsInstance() ? Reflect.Get<Stack<ICommand>>(Singleton<UndoRedoManager>.Instance, "undo") : null;
        }

        internal static int Depth()
        {
            Stack<ICommand> steps = Steps();
            return steps == null ? 0 : steps.Count;
        }

        // Push the step of a press, after taking off the steps KKPE pushed during it.
        internal static void Push(ICommand step, int depthBefore)
        {
            Stack<ICommand> steps = Steps();
            while (steps != null && steps.Count > depthBefore && steps.Peek().GetType().FullName == KkpeStep)
            {
                steps.Pop();
            }

            Singleton<UndoRedoManager>.Instance.Push(step);
        }
    }

    // One step of Studio's Undo: the pose before and after an edit, only what changed.
    internal sealed class PoseCommand : ICommand
    {
        readonly PadTarget target;
        readonly TransformsCommand guides = new TransformsCommand();
        readonly List<Transform> bones = new List<Transform>();
        readonly List<BoneOverride> oldBones = new List<BoneOverride>(), newBones = new List<BoneOverride>();

        PoseCommand(PadTarget target)
        {
            this.target = target;
        }

        internal int Count
        {
            get
            {
                return guides.Count + bones.Count;
            }
        }

        // What changed between two snapshots of the same character; null when nothing did.
        internal static PoseCommand Between(PoseSnapshot before, PoseSnapshot after)
        {
            PoseCommand command = new PoseCommand(before.Target);
            foreach (KeyValuePair<int, Vector3[]> old in before.Guides)
            {
                Vector3[] now;
                if (after.Guides.TryGetValue(old.Key, out now) && (now[0] != old.Value[0] || now[1] != old.Value[1]))
                {
                    command.guides.Add(old.Key, old.Value[0], now[0], old.Value[1], now[1]);
                }
            }

            // A bone with an override before or after (the override may have been added or removed).
            List<Transform> touched = new List<Transform>(before.Bones.Keys);
            foreach (Transform bone in after.Bones.Keys)
            {
                if (!before.Bones.ContainsKey(bone))
                {
                    touched.Add(bone);
                }
            }

            foreach (Transform bone in touched)
            {
                BoneOverride old = Lookup(before.Bones, bone), now = Lookup(after.Bones, bone);
                if (!old.Same(now))
                {
                    command.bones.Add(bone);
                    command.oldBones.Add(old);
                    command.newBones.Add(now);
                }
            }

            return command.Count > 0 ? command : null;
        }

        static BoneOverride Lookup(Dictionary<Transform, BoneOverride> overrides, Transform bone)
        {
            BoneOverride found;
            return overrides.TryGetValue(bone, out found) ? found : BoneOverride.None;
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
            if (newValues)
            {
                guides.Do();
            }
            else
            {
                guides.Undo();
            }

            if (target.Pose == null)
            {
                return;   // the character was deleted: its bones are gone too
            }

            for (int i = 0; i < bones.Count; i++)
            {
                if (bones[i] != null)
                {
                    Kkpe.SetOverrides(target, bones[i], newValues ? newBones[i] : oldBones[i]);
                }
            }
        }
    }
}

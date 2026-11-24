// 弹盒（实物界面）：浮在弓架旁的五格木盒，每格放一颗弹，格子前沿一排小圆钉 = 剩几发。右手捏哪格装哪种。
using UnityEngine;

namespace PaperSiege
{
    public class AmmoBox : MonoBehaviour
    {
        AmmoStore store;
        readonly Transform[] balls = new Transform[5];
        readonly Transform[][] pegs = new Transform[5][];
        Transform look;
        const float Cell = 0.031f;
        public const float Scale = 0.65f;   // 模型 16 cm 宽，挂在手下缩到约 10 cm

        public static AmmoBox Build(AmmoStore store)
        {
            var go = new GameObject("AmmoBox");
            var b = go.AddComponent<AmmoBox>();
            b.store = store;
            b.BuildLook();
            store.Changed += (a, n) => b.Refresh();
            b.Refresh();
            return b;
        }

        public Vector3 SlotPos(Ammo a) => transform.TransformPoint(new Vector3(((int)a - 2) * Cell, 0.017f, 0));

        void BuildLook()
        {
            var lib = BlockLibrary.I;
            var bm = lib ? lib.Meshes("A_Box") : null;
            if (bm != null && bm.lod0 && lib.propMaterial)
            {
                var g = new GameObject("Look"); g.transform.SetParent(transform, false);
                g.AddComponent<MeshFilter>().sharedMesh = bm.lod0; g.AddComponent<MeshRenderer>().sharedMaterial = lib.propMaterial;
                look = g.transform;
            }
            else
            {
                var wood = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.6f, 0.43f, 0.27f) };
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(transform, false); g.transform.localScale = new Vector3(Cell * 5.2f, 0.012f, Cell * 1.1f);
                g.GetComponent<Renderer>().sharedMaterial = wood; look = g.transform;
            }
            var pegMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.85f, 0.7f, 0.4f) };
            var pegMesh = lib ? lib.Meshes("A_Peg") : null;
            for (int i = 0; i < 5; i++)
            {
                balls[i] = BallLook.Build(transform, (Ammo)i, 0.011f);
                balls[i].localPosition = new Vector3((i - 2) * Cell, 0.017f, 0);
                pegs[i] = new Transform[AmmoStore.MaxPerType];
                for (int k = 0; k < AmmoStore.MaxPerType; k++)
                {
                    // 钉子插在格子前面的钉孔里，钉帽朝玩家（本地 -Z）
                    GameObject p;
                    if (pegMesh != null && pegMesh.lod0 && lib.propMaterial)
                    {
                        p = new GameObject("Peg");
                        p.AddComponent<MeshFilter>().sharedMesh = pegMesh.lod0;
                        p.AddComponent<MeshRenderer>().sharedMaterial = lib.propMaterial;
                        p.transform.SetParent(transform, false);
                        p.transform.localRotation = Quaternion.Euler(-90, 0, 0);
                    }
                    else
                    {
                        p = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(p.GetComponent<Collider>());
                        p.transform.SetParent(transform, false);
                        p.transform.localScale = new Vector3(0.0028f, 0.0018f, 0.0028f);
                        p.GetComponent<Renderer>().sharedMaterial = pegMat;
                    }
                    p.transform.localPosition = new Vector3((i - 2) * Cell + (k - 2) * 0.005f, 0.013f, -0.0185f);
                    pegs[i][k] = p.transform;
                }
            }
        }

        public void Refresh()
        {
            if (store == null) return;
            for (int i = 0; i < 5; i++)
            {
                int n = store.Count((Ammo)i);
                if (balls[i]) balls[i].gameObject.SetActive(n > 0);
                for (int k = 0; k < pegs[i].Length; k++) pegs[i][k].gameObject.SetActive(k < n);
            }
        }

        public void Show(bool on) => gameObject.SetActive(on);

        /// <summary>放在桌上：自己城的左前方（和右边的补给盘对称），朝向玩家。固定的实物，永远不挡视线</summary>
        public void PlaceOnTable(Arena a)
        {
            var right = Vector3.Cross(Vector3.up, a.forward);
            transform.localScale = Vector3.one;
            transform.SetPositionAndRotation(a.tableCenter - right * 0.3f - a.forward * 0.16f, Quaternion.LookRotation(a.forward, Vector3.up));
            gameObject.SetActive(true);
        }

        /// <summary>选中的那格轻轻抬起</summary>
        public void Tick()
        {
            for (int i = 0; i < 5; i++)
                if (balls[i]) balls[i].localPosition = new Vector3((i - 2) * Cell, store.selected == (Ammo)i ? 0.026f : 0.017f, 0);
        }

        /// <summary>（旧）挂在左手下方：瞄敌城时会挡住自己的城，已改成放在桌上</summary>
        public void Follow(Vector3 forkPos, Transform head)
        {
            transform.localScale = Vector3.one * Scale;
            if (!head) { transform.position = forkPos + new Vector3(0.02f, -0.12f, -0.04f); return; }
            var right = Vector3.ProjectOnPlane(head.right, Vector3.up).normalized;
            var toHeadFlat = Vector3.ProjectOnPlane(head.position - forkPos, Vector3.up).normalized;
            var target = forkPos + right * 0.03f - Vector3.up * 0.12f + toHeadFlat * 0.05f;
            transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-Time.deltaTime * 12f));
            var toHead = Vector3.ProjectOnPlane(head.position - transform.position, Vector3.up);
            if (toHead.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(-toHead.normalized, Vector3.up);
            // 选中的那格轻轻抬起
            for (int i = 0; i < 5; i++)
                if (balls[i]) balls[i].localPosition = new Vector3((i - 2) * Cell, store.selected == (Ammo)i ? 0.026f : 0.017f, 0);
        }

        /// <summary>捏合点在哪一格上（半径 r 内）</summary>
        public Ammo? Pick(Vector3 p, float r)
        {
            if (!gameObject.activeInHierarchy) return null;
            for (int i = 0; i < 5; i++)
                if (Vector3.Distance(p, SlotPos((Ammo)i)) < r) return (Ammo)i;
            return null;
        }
    }
}

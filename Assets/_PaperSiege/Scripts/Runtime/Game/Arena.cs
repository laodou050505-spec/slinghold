// 场地：桌面 + 地面（碰撞体）、两座城、敌人投石机的位置。
// 编辑器里没有真实房间：玩家站在原点面朝 +Z，桌子在身前，另外摆一个占位房间（只在编辑器里看得见）。
// 头显上：桌子从系统空间扫描读（RoomTable，D3 接入），读不到就在身前生成一张虚拟木桌。
using UnityEngine;

namespace PaperSiege
{
    public class Arena : MonoBehaviour
    {
        public Transform head;
        public Vector3 tableCenter;     // 桌面中心（世界，桌面高度）
        public Vector2 tableSize = new(0.9f, 0.6f);
        public Vector3 forward = Vector3.forward;   // 玩家面朝的水平方向（朝敌城）
        public Castle mine, enemy;
        public Transform catapultSpot;  // 投石机位置（敌城前侧，地面上）
        GameObject env;
        GameTuning T => GameTuning.I;

        /// <summary>按玩家当前站位摆场地（开局 / 重来时调用）</summary>
        public void Layout()
        {
            var hp = head ? head.position : new Vector3(0, 1.5f, 0);
            var f = head ? Vector3.ProjectOnPlane(head.forward, Vector3.up) : Vector3.forward;
            forward = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            if (!HasRealTable(out tableCenter))
                tableCenter = new Vector3(hp.x, T.tableHeight, hp.z) + forward * 0.5f;
            BuildEnv();
        }

        bool HasRealTable(out Vector3 c) { c = default; return false; }   // D3：接 RoomTable

        void BuildEnv()
        {
            if (env) Destroy(env);
            env = new GameObject("Env");
            env.transform.SetParent(transform, false);
            var rot = Quaternion.LookRotation(forward, Vector3.up);
            // 地面（无限大的一块）
            var floor = new GameObject("Floor") { layer = Layers.Env };
            floor.transform.SetParent(env.transform, false);
            floor.transform.position = new Vector3(tableCenter.x, -0.05f, tableCenter.z);
            floor.AddComponent<BoxCollider>().size = new Vector3(30, 0.1f, 30);
            // 桌面
            var table = new GameObject("TableTop") { layer = Layers.Env };
            table.transform.SetParent(env.transform, false);
            table.transform.SetPositionAndRotation(tableCenter - Vector3.up * 0.02f, rot);
            table.AddComponent<BoxCollider>().size = new Vector3(tableSize.x, 0.04f, tableSize.y);
            // 编辑器占位房间
            if (HandInput.Sim) EditorRoom(rot);
            catapultSpot = new GameObject("CatapultSpot").transform;
            catapultSpot.SetParent(env.transform, false);
        }

        void EditorRoom(Quaternion rot)
        {
            Prim("RoomFloor", new Vector3(tableCenter.x, -0.051f, tableCenter.z) + forward * 1.5f, new Vector3(6, 0.1f, 7.5f), rot, new Color(0.56f, 0.45f, 0.34f));
            Prim("RoomWall", new Vector3(tableCenter.x, 1.4f, tableCenter.z) + forward * 5.2f, new Vector3(6, 2.8f, 0.1f), rot, new Color(0.86f, 0.84f, 0.8f));
            var legH = tableCenter.y - 0.04f;
            Prim("TableTopLook", tableCenter - Vector3.up * 0.02f, new Vector3(tableSize.x, 0.04f, tableSize.y), rot, new Color(0.48f, 0.33f, 0.21f));
            foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                Prim("Leg", tableCenter + rot * new Vector3(sx * (tableSize.x / 2 - 0.04f), 0, sz * (tableSize.y / 2 - 0.04f)) - Vector3.up * (0.04f + legH / 2), new Vector3(0.05f, legH, 0.05f), rot, new Color(0.42f, 0.29f, 0.18f));
        }

        void Prim(string name, Vector3 pos, Vector3 size, Quaternion rot, Color c)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(env.transform, false);
            g.transform.SetPositionAndRotation(pos, rot);
            g.transform.localScale = size;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = c };
            m.SetFloat("_Smoothness", 0.1f);
            g.GetComponent<Renderer>().sharedMaterial = m;
        }

        public void BuildCastles(LevelDef level)
        {
            if (mine) mine.Cleanup();
            if (enemy) enemy.Cleanup();
            var rot = Quaternion.LookRotation(forward, Vector3.up);
            // 玩家城：正面（城门）朝向玩家
            mine = Castle.Build(Levels.PlayerCastle(), Side.Player, tableCenter + forward * 0.02f, rot, T.playerUnit, false, transform);
            // 敌城：在对面地上，正面朝玩家
            var ep = new Vector3(tableCenter.x, 0, tableCenter.z) + forward * T.enemyDistance;
            enemy = Castle.Build(level.enemy, Side.Enemy, ep, rot, T.enemyUnit, true, transform);
            mine.SetKingHP(3);              // 自己的帅旗 3 点耐久（不会被一发打掉）
            enemy.SetKingHP(level.kingHP);
            catapultSpot.SetPositionAndRotation(enemy.ToWorld(new Vector3(7.2f, 0, -2.0f)), Quaternion.LookRotation(forward, Vector3.up));   // 投石机正面是本地 -Z → 朝玩家
        }
    }
}

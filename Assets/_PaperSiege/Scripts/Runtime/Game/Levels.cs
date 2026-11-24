// 10 关数据（方案第 6 节）：敌城布局、开局弹药、敌人打什么弹、开火间隔、天色时段、这一关的“新东西”。
// 城的布局用几个搭建函数拼：墙、角楼、主楼（帅旗在顶上）、石墙、盾墙。单位：格。敌城正面朝本地 -Z（对着玩家）。
using System.Collections.Generic;
using UnityEngine;

namespace PaperSiege
{
    public enum Ammo { Stone, Fire, Ice, Bomb, Split }

    public class LevelDef
    {
        public int index;
        public string title;                    // 中文原文（Loc 的键）
        public CastleLayout enemy;
        public Dictionary<Ammo, int> start = new();
        public Ammo[] enemyAmmo = { Ammo.Stone };  // 敌人轮流打的弹
        public Vector2 fireEvery = new(4.5f, 6f);
        public float timeLimit = 180f;
        public Ammo? newAmmo;                   // 这一关第一次出现的弹（出说明木牌）
        public bool tutorial;
        public int timeOfDay;                   // 0 清晨 1 上午 2 午后 3 黄昏 4 入夜 5 烟火夜
        public float aimError = 1.0f;           // 敌人瞄准误差（格）
        public int kingHP = 1;                  // 敌方帅旗耐久：直接打中 -1、炸丸 -2、冻住时挨打多 -1
    }

    public static class Levels
    {
        public const int Count = 10;

        // ── 搭建函数
        static void Tower(CastleLayout L, float x, float z, int floors, Paint a, Paint b, bool cone = true, bool round = false)
        {
            float y = 0;
            for (int i = 0; i < floors; i++)
            {
                if (round) { L.Add("W_Column", i % 2 == 0 ? a : b, x, y, z); y += 2; }
                else if (i == floors - 1) { L.Add("W_Window", b, x, y, z); y += 1; }
                else { L.Add("W_Pillar", i % 2 == 0 ? a : b, x, y, z); y += 2; }
            }
            if (cone) L.Add("W_Cone", Paint.Brick, x, y, z, 15 * x);
        }

        static void Wall(CastleLayout L, float x0, float x1, float z, int rows, Paint a, Paint b, bool battlement = true)
        {
            // 每段 2 格宽：底下方块 / 窗块，顶上雉堞
            for (float x = x0; x < x1 - 0.5f; x += 2)
            {
                for (int r = 0; r < rows; r++)
                {
                    L.Add(r == rows - 1 && rows > 1 ? "W_Window" : "W_Cube", (r + (int)x) % 2 == 0 ? a : b, x + 0.5f, r, z);
                    L.Add("W_Cube", (r + (int)x) % 2 == 0 ? b : a, x + 1.5f, r, z);
                }
                if (battlement) L.Add("W_Battlement", a, x + 1, rows, z);
            }
        }

        /// <summary>主楼：拱门 + 木板 + 若干层方块 / 窗块，帅旗在顶上</summary>
        static void Keep(CastleLayout L, float x, float z, int floors, Paint a, Paint b)
        {
            L.Add("W_Arch", a, x, 0, z);
            L.Add("W_Slab", b, x, 1, z);
            float y = 1.5f;
            for (int i = 0; i < floors; i++) { L.Add(i == floors - 1 ? "W_Window" : "W_Cube", i % 2 == 0 ? b : a, x, y, z); y += 1; }
            L.king = new Vector3(x, y, z);
        }

        static void StoneWall(CastleLayout L, float x0, float x1, float z, int rows, bool battlement = true)
        {
            for (float x = x0; x < x1 - 0.5f; x += 2)
            {
                for (int r = 0; r < rows; r++) L.Add("S_Wall", Paint.Stone, x + 1, r, z);
                if (battlement) L.Add("S_Battlement", Paint.Stone, x + 1, rows, z);
            }
        }

        /// <summary>
        /// 空心小堡：3×3 格一圈积木把帅旗围在正中那格（两层高，旗杆顶露出一截：看得见、打不到旗身）。
        /// stone = 石块砌（火烧不动，要靠炸）；frontHigh = 正面再加一层（挡住平射，只能高抛或者先拆墙）。
        /// </summary>
        static void Shrine(CastleLayout L, float x, float z, bool stone, bool frontHigh, Paint a, Paint b)
        {
            string piece = stone ? "S_Cube" : "W_Cube";
            for (int y = 0; y < 2; y++)
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        L.Add(dz == -1 && dx == 0 && y == 1 && !stone ? "W_Window" : piece, stone ? Paint.Stone : ((dx + dz + y) & 1) == 0 ? a : b, x + dx, y, z + dz);
                    }
            if (frontHigh)
                for (int dx = -1; dx <= 1; dx++) L.Add(piece, stone ? Paint.Stone : (dx & 1) == 0 ? b : a, x + dx, 2, z - 1);
            // 中间垫一块旗座，把帅旗抬高一格：旗面露出墙头 —— 看得见、平射打不着、高抛能打到
            // （以前只有“正面加高”的小堡才垫，导致第 3～5 关的旗面缩在围墙里，两档机器人都 0% 通不过）
            L.Add(piece, stone ? Paint.Stone : a, x, 0, z);
            L.king = new Vector3(x, 1, z);
        }

        static void Hall(CastleLayout L, float x, float z, Paint a)
        {
            L.Add("W_Arch", a, x, 0, z);
            L.Add("W_Roof", Paint.Brick, x, 1, z);
        }

        /// <summary>玩家自己的城（每关一样，放在桌上）</summary>
        public static CastleLayout PlayerCastle()
        {
            var L = new CastleLayout("Mine");
            L.Add("W_Gate", Paint.Cream, 0, 0, -1);
            L.Add("W_Battlement", Paint.Teal, 0, 1.5f, -1);
            foreach (int s in new[] { -1, 1 })
            {
                L.Add("W_Cube", s < 0 ? Paint.Teal : Paint.Ochre, 1.5f * s, 0, -1);
                L.Add("W_Window", s < 0 ? Paint.Ochre : Paint.Teal, 1.5f * s, 1, -1);
                L.Add("W_Pillar", s < 0 ? Paint.Cream : Paint.Brick, 2.5f * s, 0, -1);
                L.Add("W_Window", s < 0 ? Paint.Teal : Paint.Cream, 2.5f * s, 2, -1);
                L.Add("W_Cone", Paint.Brick, 2.5f * s, 3, -1, 37 * s);
            }
            Keep(L, 0, 0.4f, 2, Paint.Blue, Paint.Ochre);
            L.Add("W_Column", Paint.Sage, -2.1f, 0, 1.0f);
            L.Add("W_Cone", Paint.Brick, -2.1f, 2, 1.0f, 70);
            L.Add("W_Column", Paint.Teal, 2.1f, 0, 1.0f);
            L.Add("W_Cone", Paint.Brick, 2.1f, 2, 1.0f, -70);
            return L;
        }

        /// <summary>检查布局里有没有互相重叠的块（按标称尺寸的包围盒，允许贴着）。返回问题列表</summary>
        public static System.Collections.Generic.List<string> Overlaps(CastleLayout L)
        {
            var boxes = new System.Collections.Generic.List<(string n, Bounds b)>();
            foreach (var p in L.items) boxes.Add((p.piece, BoxOf(p.piece, p.pos, p.yaw)));
            boxes.Add(("King", BoxOf("King", L.king, 0)));
            var res = new System.Collections.Generic.List<string>();
            for (int i = 0; i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    var a = boxes[i].b; var b = boxes[j].b;
                    var lo = Vector3.Max(a.min, b.min); var hi = Vector3.Min(a.max, b.max);
                    var o = hi - lo;
                    if (o.x > 0.06f && o.y > 0.06f && o.z > 0.06f) res.Add($"{boxes[i].n}@{a.center} × {boxes[j].n}@{b.center} 重叠 {o}");
                }
            return res;
        }

        static Bounds BoxOf(string piece, Vector3 pos, float yaw)
        {
            var d = BlockLibrary.Def(piece);
            var s = d.size;
            if (piece != "W_Cone" && Mathf.Abs(Mathf.DeltaAngle(yaw, 90)) < 1 || Mathf.Abs(Mathf.DeltaAngle(yaw, 270)) < 1) s = new Vector3(s.z, s.y, s.x);
            if (piece == "W_Cone") s = new Vector3(1f, s.y, 1f);   // 尖顶的檐口伸出去一点，按塔身宽算
            return new Bounds(pos + Vector3.up * s.y * 0.5f, s);
        }

        public static LevelDef Get(int i)
        {
            var d = new LevelDef { index = i };
            var L = new CastleLayout("L" + (i + 1));
            d.enemy = L;
            switch (i)
            {
                case 0:   // 教学：一堵矮墙 + 帅旗
                    d.title = "第一关 · 矮墙"; d.tutorial = true; d.timeOfDay = 0;
                    L.Add("W_Cube", Paint.Teal, -1, 0, 0); L.Add("W_Cube", Paint.Ochre, 1, 0, 0);
                    L.Add("W_Slab", Paint.Cream, 0, 1, 0);
                    L.Add("W_Arch", Paint.Brick, 0, 0, -1.6f); L.Add("W_Battlement", Paint.Teal, 0, 1, -1.6f);
                    L.king = new Vector3(0, 1.5f, 0);
                    d.start[Ammo.Stone] = 6; d.fireEvery = new Vector2(6f, 7.5f); d.aimError = 1.6f;
                    break;
                case 1:
                    d.title = "第二关 · 小城"; d.timeOfDay = 0;
                    Wall(L, -2, 2, -1.5f, 1, Paint.Cream, Paint.Teal);
                    Keep(L, 0, 0.3f, 1, Paint.Blue, Paint.Ochre);
                    Tower(L, -2.6f, -1.5f, 1, Paint.Ochre, Paint.Teal); Tower(L, 2.6f, -1.5f, 1, Paint.Brick, Paint.Cream);
                    d.start[Ammo.Stone] = 6; d.fireEvery = new Vector2(5.5f, 7f); d.aimError = 1.4f;
                    break;
                case 2:
                    d.title = "第三关 · 木楼"; d.timeOfDay = 1; d.newAmmo = Ammo.Fire; d.kingHP = 3;
                    Wall(L, -3, 3, -2.6f, 1, Paint.Ochre, Paint.Cream);
                    Tower(L, -3.6f, -2.6f, 1, Paint.Teal, Paint.Cream); Tower(L, 3.6f, -2.6f, 1, Paint.Brick, Paint.Cream);
                    Hall(L, -2.6f, 1.2f, Paint.Sage); Hall(L, 2.6f, 1.2f, Paint.Blue);
                    L.Add("W_Beam", Paint.Cream, 0, 0, 2.6f);
                    Shrine(L, 0, 0, false, false, Paint.Teal, Paint.Cream);
                    d.start[Ammo.Stone] = 5; d.enemyAmmo = new[] { Ammo.Stone, Ammo.Fire, Ammo.Stone, Ammo.Fire };
                    break;
                case 3:
                    d.title = "第四关 · 着火"; d.timeOfDay = 1; d.kingHP = 3;
                    Wall(L, -3, 3, -2.6f, 1, Paint.Brick, Paint.Ochre);
                    Tower(L, -3.6f, -2.6f, 2, Paint.Cream, Paint.Teal); Tower(L, 3.6f, -2.6f, 2, Paint.Teal, Paint.Cream);
                    Hall(L, -2.6f, 1.2f, Paint.Ochre); Hall(L, 2.6f, 1.2f, Paint.Sage);
                    Keep(L, 0, 2.6f, 2, Paint.Blue, Paint.Cream);   // 诱饵：高楼上没有旗
                    Shrine(L, 0, 0, false, false, Paint.Blue, Paint.Ochre);
                    d.start[Ammo.Stone] = 5; d.start[Ammo.Fire] = 1; d.enemyAmmo = new[] { Ammo.Fire, Ammo.Stone, Ammo.Fire };
                    break;
                case 4:
                    d.title = "第五关 · 高塔"; d.timeOfDay = 2; d.newAmmo = Ammo.Ice; d.kingHP = 3;
                    Wall(L, -3, 3, -2.6f, 2, Paint.Cream, Paint.Teal);
                    Tower(L, -3.6f, -2.6f, 3, Paint.Ochre, Paint.Cream); Tower(L, 3.6f, -2.6f, 3, Paint.Teal, Paint.Cream);
                    Tower(L, -2.6f, 1.6f, 1, Paint.Sage, Paint.Teal, true, true); Tower(L, 2.6f, 1.6f, 1, Paint.Blue, Paint.Teal, true, true);
                    Keep(L, 0, 2.6f, 3, Paint.Brick, Paint.Ochre);
                    Shrine(L, 0, 0, false, false, Paint.Cream, Paint.Brick);
                    d.start[Ammo.Stone] = 5; d.enemyAmmo = new[] { Ammo.Stone, Ammo.Ice, Ammo.Fire, Ammo.Ice };
                    break;
                case 5:
                    d.title = "第六关 · 双层"; d.timeOfDay = 2; d.kingHP = 4;
                    Wall(L, -4, 4, -3.2f, 2, Paint.Teal, Paint.Cream);
                    Tower(L, -4.6f, -3.2f, 3, Paint.Cream, Paint.Ochre); Tower(L, 4.6f, -3.2f, 3, Paint.Brick, Paint.Cream);
                    Hall(L, -2.8f, 0.4f, Paint.Sage); Hall(L, 2.8f, 0.4f, Paint.Teal);
                    Keep(L, 0, 2.6f, 3, Paint.Blue, Paint.Cream);
                    Shrine(L, 0, 0, false, true, Paint.Ochre, Paint.Brick);
                    d.start[Ammo.Stone] = 4; d.start[Ammo.Fire] = 2; d.enemyAmmo = new[] { Ammo.Fire, Ammo.Ice, Ammo.Stone };
                    break;
                case 6:
                    d.title = "第七关 · 石墙"; d.timeOfDay = 3; d.newAmmo = Ammo.Bomb; d.kingHP = 4;
                    StoneWall(L, -4, 4, -3.4f, 1);
                    Tower(L, -3.4f, -1.0f, 3, Paint.Teal, Paint.Cream); Tower(L, 3.4f, -1.0f, 3, Paint.Ochre, Paint.Cream);
                    Hall(L, -2.6f, 2.4f, Paint.Sage); Hall(L, 2.6f, 2.4f, Paint.Blue);
                    Keep(L, 0, 2.6f, 3, Paint.Brick, Paint.Ochre);
                    Shrine(L, 0, 0, true, true, Paint.Stone, Paint.Stone);
                    d.start[Ammo.Stone] = 4; d.start[Ammo.Fire] = 1; d.start[Ammo.Ice] = 1; d.enemyAmmo = new[] { Ammo.Bomb, Ammo.Stone, Ammo.Fire, Ammo.Bomb };
                    break;
                case 7:
                    d.title = "第八关 · 盾墙"; d.timeOfDay = 3; d.newAmmo = Ammo.Split; d.kingHP = 4;
                    L.Add("S_Shield", Paint.Stone, 0, 0, -4.6f);
                    StoneWall(L, -5, -1, -3.4f, 1); StoneWall(L, 1, 5, -3.4f, 1);
                    Tower(L, -3.6f, -1.0f, 3, Paint.Cream, Paint.Brick); Tower(L, 3.6f, -1.0f, 3, Paint.Brick, Paint.Cream);
                    Hall(L, -2.6f, 2.4f, Paint.Ochre); Hall(L, 2.6f, 2.4f, Paint.Sage);
                    Keep(L, 0, 2.6f, 3, Paint.Blue, Paint.Cream);
                    Shrine(L, 0, 0, true, true, Paint.Stone, Paint.Stone);
                    d.start[Ammo.Stone] = 4; d.start[Ammo.Bomb] = 1; d.enemyAmmo = new[] { Ammo.Split, Ammo.Stone, Ammo.Bomb, Ammo.Fire };
                    break;
                case 8:
                    d.title = "第九关 · 要塞"; d.timeOfDay = 4; d.kingHP = 5;
                    L.Add("S_Shield", Paint.Stone, 0, 0, -4.8f);
                    StoneWall(L, -5, -1, -3.6f, 2); StoneWall(L, 1, 5, -3.6f, 2);
                    Wall(L, -3, 3, -2.2f, 2, Paint.Ochre, Paint.Cream);
                    Tower(L, -4.4f, -2.2f, 3, Paint.Teal, Paint.Cream); Tower(L, 4.4f, -2.2f, 3, Paint.Cream, Paint.Teal);
                    Tower(L, -2.6f, 1.4f, 2, Paint.Sage, Paint.Blue, true, true); Tower(L, 2.6f, 1.4f, 2, Paint.Blue, Paint.Sage, true, true);
                    Keep(L, 0, 2.6f, 4, Paint.Brick, Paint.Teal);
                    Shrine(L, 0, 0, true, true, Paint.Stone, Paint.Stone);
                    d.start[Ammo.Stone] = 4; d.start[Ammo.Fire] = 1; d.start[Ammo.Ice] = 1; d.start[Ammo.Bomb] = 1;
                    d.enemyAmmo = new[] { Ammo.Fire, Ammo.Bomb, Ammo.Ice, Ammo.Split, Ammo.Stone };
                    d.fireEvery = new Vector2(4f, 5.5f); d.aimError = 0.8f;
                    break;
                default:  // 第十关：终局大城，帅旗在两道墙后的石砌小堡里
                    d.title = "第十关 · 王城"; d.timeOfDay = 5; d.kingHP = 5;
                    L.Add("S_Shield", Paint.Stone, -2.2f, 0, -5.6f); L.Add("S_Shield", Paint.Stone, 2.2f, 0, -5.6f);
                    StoneWall(L, -6, 6, -4.4f, 2);
                    Wall(L, -4, 4, -2.6f, 3, Paint.Cream, Paint.Teal);
                    Tower(L, -5f, -2.6f, 4, Paint.Teal, Paint.Cream); Tower(L, 5f, -2.6f, 4, Paint.Cream, Paint.Teal);
                    Tower(L, -2.8f, 1.2f, 2, Paint.Sage, Paint.Blue, true, true); Tower(L, 2.8f, 1.2f, 2, Paint.Blue, Paint.Sage, true, true);
                    Hall(L, -2.6f, 3.4f, Paint.Ochre); Hall(L, 2.6f, 3.4f, Paint.Brick);
                    Keep(L, 0, 2.8f, 5, Paint.Blue, Paint.Cream);
                    Shrine(L, 0, 0, true, true, Paint.Stone, Paint.Stone);
                    d.start[Ammo.Stone] = 5; d.start[Ammo.Fire] = 1; d.start[Ammo.Ice] = 1; d.start[Ammo.Bomb] = 1; d.start[Ammo.Split] = 1;
                    d.enemyAmmo = new[] { Ammo.Fire, Ammo.Bomb, Ammo.Split, Ammo.Ice, Ammo.Stone };
                    d.fireEvery = new Vector2(3.8f, 5f); d.aimError = 0.7f;
                    break;
            }
            return d;
        }
    }
}

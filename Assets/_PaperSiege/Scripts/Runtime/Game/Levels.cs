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
        public string mechTip;                  // 这一关第一次出现的机关：说明木牌的字（中文原文），机关会闪几下指出位置
        public string mechPiece;                // 要闪的机关零件名
        public bool tutorial;
        public int timeOfDay;                   // 0 清晨 1 上午 2 午后 3 黄昏 4 入夜 5 烟火夜
        public float aimError = 1.0f;           // 敌人瞄准误差（格）
        public int kingHP = 1;                  // 敌方帅旗耐久：直接打中 -1、炸丸 -2、冻住时挨打多 -1
        public float kingFocus = 0.1f;          // 敌人专打我方帅旗下方那一列的概率（前期主要拆墙，后期才盯帅旗）
    }

    public static class Levels
    {
        public const int Count = 18;   // 11-27：六个时段（清晨 → 烟火夜）各 3 关；每章第一关新弹登场，后两关是这种弹的机关

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
        static void Shrine(CastleLayout L, float x, float z, bool stone, bool frontHigh, Paint a, Paint b, float y0 = 0f)
        {
            string piece = stone ? "S_Cube" : "W_Cube";
            for (int y = 0; y < 2; y++)
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        L.Add(dz == -1 && dx == 0 && y == 1 && !stone ? "W_Window" : piece, stone ? Paint.Stone : ((dx + dz + y) & 1) == 0 ? a : b, x + dx, y0 + y, z + dz);
                    }
            if (frontHigh)
                for (int dx = -1; dx <= 1; dx++) L.Add(piece, stone ? Paint.Stone : (dx & 1) == 0 ? b : a, x + dx, y0 + 2, z - 1);
            // 中间垫一块旗座，把帅旗抬高一格：旗面露出墙头 —— 看得见、平射打不着、高抛能打到
            // （以前只有“正面加高”的小堡才垫，导致第 3～5 关的旗面缩在围墙里，两档机器人都 0% 通不过）
            L.Add(piece, stone ? Paint.Stone : a, x, y0, z);
            L.king = new Vector3(x, y0 + 1, z);
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
            foreach (var p in L.items) if (p.piece != "M_Bell" && p.piece != "M_ScaleBeam" && p.piece != "M_Trough") boxes.Add((p.piece, BoxOf(p.piece, p.pos, p.yaw)));   // 吊着 / 原点在转轴的机关、上面要放东西的冰池不参与
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

        /// <summary>
        /// 拱桥：两座石拱中间卡一块拱心石，上面两块石板 → 平台（石板顶 1.5 格）。拱心石一没，平台和上面的东西整片塌
        /// </summary>
        static void Bridge(CastleLayout L, float x, float z)
        {
            L.Add("S_Arch", Paint.Stone, x - 1.5f, 0, z);
            L.Add("S_Arch", Paint.Stone, x + 1.5f, 0, z);
            L.Add("M_Keystone", Paint.Stone, x, 0.4f, z);
            L.Add("S_Slab", Paint.Stone, x - 1f, 1f, z);
            L.Add("S_Slab", Paint.Stone, x + 1f, 1f, z);
        }

        // 原来的 10 关城型（编号改了，城不变）
        static void TutorialWall(CastleLayout L)
        {
            L.Add("W_Cube", Paint.Teal, -1, 0, 0); L.Add("W_Cube", Paint.Ochre, 1, 0, 0);
            L.Add("W_Slab", Paint.Cream, 0, 1, 0);
            L.Add("W_Arch", Paint.Brick, 0, 0, -1.6f); L.Add("W_Battlement", Paint.Teal, 0, 1, -1.6f);
            L.king = new Vector3(0, 1.5f, 0);
        }
        static void LittleKeep(CastleLayout L)
        {
            Wall(L, -2, 2, -1.5f, 1, Paint.Cream, Paint.Teal);
            Keep(L, 0, 0.3f, 1, Paint.Blue, Paint.Ochre);
            Tower(L, -2.6f, -1.5f, 1, Paint.Ochre, Paint.Teal); Tower(L, 2.6f, -1.5f, 1, Paint.Brick, Paint.Cream);
        }
        static void TimberHall(CastleLayout L)
        {
            Wall(L, -3, 3, -2.6f, 1, Paint.Ochre, Paint.Cream);
            Tower(L, -3.6f, -2.6f, 1, Paint.Teal, Paint.Cream); Tower(L, 3.6f, -2.6f, 1, Paint.Brick, Paint.Cream);
            Hall(L, -2.6f, 1.2f, Paint.Sage); Hall(L, 2.6f, 1.2f, Paint.Blue);
            L.Add("W_Beam", Paint.Cream, 0, 0, 2.6f);
            Shrine(L, 0, 0, false, false, Paint.Teal, Paint.Cream);
        }

        public static LevelDef Get(int i)
        {
            var d = new LevelDef { index = i };
            var L = new CastleLayout("L" + (i + 1));
            d.enemy = L;
            switch (i)
            {
                // ═══════════ 清晨：学会打（只有石丸）
                case 0:   // 教学：一堵矮墙 + 帅旗
                    d.title = "第一关 · 矮墙"; d.tutorial = true; d.timeOfDay = 0;
                    TutorialWall(L);
                    d.start[Ammo.Stone] = 6; d.fireEvery = new Vector2(6f, 7.5f); d.aimError = 1.6f;
                    break;
                case 1:
                    d.title = "第二关 · 小城"; d.timeOfDay = 0;
                    LittleKeep(L);
                    d.start[Ammo.Stone] = 6; d.fireEvery = new Vector2(5.5f, 7f); d.aimError = 1.4f;
                    break;
                case 2:
                    d.title = "第三关 · 木楼"; d.timeOfDay = 0; d.kingHP = 2;
                    TimberHall(L);
                    d.start[Ammo.Stone] = 7; d.fireEvery = new Vector2(5.5f, 7f); d.aimError = 1.3f;
                    break;
                // ═══════════ 上午：火丸（草垛、火药桶）
                case 3:
                    d.title = "第四关 · 着火"; d.timeOfDay = 1; d.newAmmo = Ammo.Fire; d.kingHP = 3;
                    Wall(L, -3, 3, -2.6f, 1, Paint.Brick, Paint.Ochre);
                    Tower(L, -3.6f, -2.6f, 2, Paint.Cream, Paint.Teal); Tower(L, 3.6f, -2.6f, 2, Paint.Teal, Paint.Cream);
                    Hall(L, -2.6f, 1.2f, Paint.Ochre); Hall(L, 2.6f, 1.2f, Paint.Sage);
                    Keep(L, 0, 2.6f, 2, Paint.Blue, Paint.Cream);   // 诱饵：高楼上没有旗
                    Shrine(L, 0, 0, false, false, Paint.Blue, Paint.Ochre);
                    d.start[Ammo.Stone] = 5; d.start[Ammo.Fire] = 2; d.enemyAmmo = new[] { Ammo.Stone, Ammo.Fire, Ammo.Stone, Ammo.Fire };
                    break;
                case 4:   // 草垛连营：小堡前面一排草垛首尾相接，点着最外面那个，火一路烧进小堡
                    d.title = "第五关 · 草垛"; d.timeOfDay = 1; d.kingHP = 3;
                    d.mechTip = "草垛：一点就着\n一排烧过去"; d.mechPiece = "M_Hay";
                    // 小堡（木头，正面加高打不进）前面一排草垛，最外面两个在矮墙外头 —— 点着外头那个，一路烧到小堡
                    Wall(L, -4, -2, -3.4f, 1, Paint.Cream, Paint.Teal, false); Wall(L, 2, 4, -3.4f, 1, Paint.Teal, Paint.Cream, false);
                    L.Add("M_Hay", Paint.Ochre, -1.1f, 0, -4.3f); L.Add("M_Hay", Paint.Ochre, 0, 0, -4.3f); L.Add("M_Hay", Paint.Ochre, 1.1f, 0, -4.3f);
                    L.Add("M_Hay", Paint.Ochre, 0, 0, -3.35f); L.Add("M_Hay", Paint.Ochre, 0, 0, -2.4f);
                    Hall(L, -3.4f, 1.0f, Paint.Sage); Hall(L, 3.4f, 1.0f, Paint.Brick);
                    Shrine(L, 0, 0, false, true, Paint.Ochre, Paint.Cream);
                    d.start[Ammo.Stone] = 4; d.start[Ammo.Fire] = 2; d.enemyAmmo = new[] { Ammo.Stone, Ammo.Fire, Ammo.Stone };
                    break;
                case 5:   // 火药桶：石砌小堡（烧不动），正面城墙中间开一个口，口里塞两只火药桶顶着小堡 —— 正面就能打到桶，一炸连堡带墙
                    d.title = "第六关 · 火药桶"; d.timeOfDay = 1; d.kingHP = 4;
                    d.mechTip = "火药桶：着火就炸\n用火丸点它"; d.mechPiece = "M_Keg";
                    Wall(L, -5, -1, -3.2f, 1, Paint.Teal, Paint.Cream); Wall(L, 1, 5, -3.2f, 1, Paint.Cream, Paint.Teal);   // Wall 每段 2 格：−5～−1、1～5，中间留 2 格的口
                    Tower(L, -5.6f, -3.2f, 2, Paint.Cream, Paint.Ochre); Tower(L, 5.6f, -3.2f, 2, Paint.Brick, Paint.Cream);
                    Shrine(L, 0, 0, true, true, Paint.Stone, Paint.Stone);
                    L.Add("M_Keg", Paint.Brick, -0.45f, 0, -2.2f); L.Add("M_Keg", Paint.Brick, 0.45f, 0, -2.2f);   // 城墙缺口里、小堡正前方
                    d.start[Ammo.Stone] = 4; d.start[Ammo.Fire] = 2; d.enemyAmmo = new[] { Ammo.Fire, Ammo.Stone, Ammo.Fire };
                    break;
                // ═══════════ 午后：冰丸（水桶、冰池 + 冻住投石机）
                case 6:
                    d.title = "第七关 · 冰丸"; d.timeOfDay = 2; d.newAmmo = Ammo.Ice; d.kingHP = 3;
                    Wall(L, -3, 3, -2.6f, 2, Paint.Cream, Paint.Teal);
                    Tower(L, -3.6f, -2.6f, 3, Paint.Ochre, Paint.Cream); Tower(L, 3.6f, -2.6f, 3, Paint.Teal, Paint.Cream);
                    Tower(L, -2.6f, 1.6f, 1, Paint.Sage, Paint.Teal, true, true); Tower(L, 2.6f, 1.6f, 1, Paint.Blue, Paint.Teal, true, true);
                    Keep(L, 0, 2.6f, 3, Paint.Brick, Paint.Ochre);
                    Shrine(L, 0, 0, false, false, Paint.Cream, Paint.Brick);
                    d.start[Ammo.Stone] = 5; d.start[Ammo.Ice] = 2; d.enemyAmmo = new[] { Ammo.Stone, Ammo.Ice, Ammo.Fire, Ammo.Ice };
                    break;
                case 7:   // 水桶：石砌小堡打不动，墙头一只水桶 —— 冰丸打桶，周围一圈冻脆，石丸一碰就碎
                    d.title = "第八关 · 水桶"; d.timeOfDay = 2; d.kingHP = 4;
                    d.mechTip = "水桶：冰丸打它\n周围冻脆一碰就碎"; d.mechPiece = "M_Bucket";
                    StoneWall(L, -4, 4, -3.2f, 1);
                    Shrine(L, 0, 0, true, true, Paint.Stone, Paint.Stone);
                    L.Add("M_Bucket", Paint.Teal, 0, 3, -1);
                    Tower(L, -3.4f, 0.6f, 2, Paint.Teal, Paint.Cream); Tower(L, 3.4f, 0.6f, 2, Paint.Ochre, Paint.Cream);
                    d.start[Ammo.Stone] = 5; d.start[Ammo.Ice] = 2; d.enemyAmmo = new[] { Ammo.Ice, Ammo.Stone, Ammo.Fire };
                    break;
                case 8:   // 冰池：帅旗在一座高木楼顶上，木楼立在浅水池里 —— 冰丸冻住池子，楼底一滑就倒；也可以冻住投石机让它停火
                    d.title = "第九关 · 冰池"; d.timeOfDay = 2; d.kingHP = 5;
                    d.mechTip = "冰池：冻住它\n上面的楼会滑倒"; d.mechPiece = "M_Trough";
                    Wall(L, -6, -2, -3.4f, 1, Paint.Ochre, Paint.Cream); Wall(L, 2, 6, -3.4f, 1, Paint.Cream, Paint.Ochre);   // 正面不砌墙：整片水面要看得见、打得着（中间那道墙会把水面全挡住）
                    L.Add("M_Trough", Paint.Teal, 0, 0, 0);
                    L.Add("W_Pillar", Paint.Cream, -0.5f, 0.22f, 0); L.Add("W_Pillar", Paint.Teal, 0.5f, 0.22f, 0);   // 立在水面上（0.22 格）
                    L.Add("W_Pillar", Paint.Ochre, -0.5f, 2.22f, 0); L.Add("W_Pillar", Paint.Brick, 0.5f, 2.22f, 0);
                    L.Add("W_Slab", Paint.Blue, 0, 4.22f, 0);
                    L.king = new Vector3(0, 4.72f, 0);
                    Tower(L, -3.6f, 1.6f, 2, Paint.Sage, Paint.Cream); Tower(L, 3.6f, 1.6f, 2, Paint.Blue, Paint.Cream);
                    d.start[Ammo.Stone] = 4; d.start[Ammo.Ice] = 3; d.enemyAmmo = new[] { Ammo.Stone, Ammo.Ice, Ammo.Stone, Ammo.Fire };
                    break;
                // ═══════════ 黄昏：炸丸（火药桶连环、拱心石）
                case 9:
                    d.title = "第十关 · 炸丸"; d.timeOfDay = 3; d.newAmmo = Ammo.Bomb; d.kingHP = 4;
                    StoneWall(L, -4, 4, -3.4f, 1);
                    Tower(L, -3.4f, -1.0f, 3, Paint.Teal, Paint.Cream); Tower(L, 3.4f, -1.0f, 3, Paint.Ochre, Paint.Cream);
                    Hall(L, -2.6f, 2.4f, Paint.Sage); Hall(L, 2.6f, 2.4f, Paint.Blue);
                    Keep(L, 0, 2.6f, 3, Paint.Brick, Paint.Ochre);
                    Shrine(L, 0, 0, true, true, Paint.Stone, Paint.Stone);
                    d.start[Ammo.Stone] = 4; d.start[Ammo.Bomb] = 2; d.start[Ammo.Ice] = 1; d.enemyAmmo = new[] { Ammo.Bomb, Ammo.Stone, Ammo.Fire, Ammo.Bomb };
                    break;
                case 10:  // 火药连环：石墙后面一排火药桶一直排到小堡边上 —— 炸第一个，一个接一个炸过去
                    d.title = "第十一关 · 连环"; d.timeOfDay = 3; d.kingHP = 4;
                    d.mechTip = "火药桶挨着火药桶\n炸一个响一串"; d.mechPiece = "M_Keg";
                    StoneWall(L, -5, -1, -3.6f, 2); StoneWall(L, 1, 5, -3.6f, 2);   // 中间留个口：从口里能看到 / 打到第一只桶
                    for (int k = 0; k < 4; k++) L.Add("M_Keg", Paint.Brick, -4.5f + k * 0.92f, 0, -2.6f + k * 0.2f);   // 石墙后面一排，最后一只在小堡左前角外（都不和墙 / 小堡重叠）
                    Shrine(L, 0, 0, true, true, Paint.Stone, Paint.Stone);
                    Tower(L, 3.6f, 0.4f, 3, Paint.Cream, Paint.Teal);
                    d.start[Ammo.Stone] = 4; d.start[Ammo.Bomb] = 1; d.start[Ammo.Fire] = 1; d.enemyAmmo = new[] { Ammo.Bomb, Ammo.Stone, Ammo.Fire };
                    break;
                case 11:  // 拱桥：帅旗的小堡搭在石拱桥上，桥中间卡着一块拱心石 —— 炸掉拱心石，整座桥连堡一起塌
                    d.title = "第十二关 · 拱桥"; d.timeOfDay = 3; d.kingHP = 6;
                    d.mechTip = "拱心石：炸掉它\n整座桥都会塌"; d.mechPiece = "M_Keystone";
                    StoneWall(L, -5, -1.5f, -3.4f, 1); StoneWall(L, 1.5f, 5, -3.4f, 1);
                    Bridge(L, 0, 0);
                    Shrine(L, 0, 0, true, false, Paint.Stone, Paint.Stone, 1.5f);
                    Tower(L, -4.2f, 0.4f, 2, Paint.Teal, Paint.Cream); Tower(L, 4.2f, 0.4f, 2, Paint.Ochre, Paint.Cream);
                    d.start[Ammo.Stone] = 4; d.start[Ammo.Bomb] = 2; d.enemyAmmo = new[] { Ammo.Bomb, Ammo.Stone, Ammo.Ice };
                    break;
                // ═══════════ 入夜：分裂丸（三口钟、吊秤）
                case 12:
                    d.title = "第十三关 · 分裂"; d.timeOfDay = 4; d.newAmmo = Ammo.Split; d.kingHP = 4;
                    L.Add("S_Shield", Paint.Stone, 0, 0, -4.6f);
                    StoneWall(L, -5, -1, -3.4f, 1); StoneWall(L, 1, 5, -3.4f, 1);
                    Tower(L, -3.6f, -1.0f, 3, Paint.Cream, Paint.Brick); Tower(L, 3.6f, -1.0f, 3, Paint.Brick, Paint.Cream);
                    Hall(L, -2.6f, 2.4f, Paint.Ochre); Hall(L, 2.6f, 2.4f, Paint.Sage);
                    Keep(L, 0, 2.6f, 3, Paint.Blue, Paint.Cream);
                    Shrine(L, 0, 0, true, true, Paint.Stone, Paint.Stone);
                    d.start[Ammo.Stone] = 4; d.start[Ammo.Split] = 2; d.start[Ammo.Bomb] = 1; d.enemyAmmo = new[] { Ammo.Split, Ammo.Stone, Ammo.Bomb, Ammo.Fire };
                    break;
                case 13:  // 三口钟：帅旗被盾墙挡着，前面钟架上挂三口铜钟 —— 1.5 秒内三口都响（分裂丸一发三颗），盾墙倒下
                    d.title = "第十四关 · 三钟"; d.timeOfDay = 4; d.kingHP = 3;   // 主要难在开门：门开了再打 3 下
                    d.mechTip = "分裂丸高抛三口钟\n一起响盾墙就倒"; d.mechPiece = "M_Bell";
                    L.Add("M_BellFrame", Paint.Bare, 0, 0, -3.8f);
                    foreach (var bx in new[] { -1.7f, 0f, 1.7f }) L.Add("M_Bell", Paint.Stone, bx, 2.04f, -3.8f);   // 挂环顶（原点上 0.14 格）挂在钟架挂钩上（2.18 格）
                    L.Add("S_Shield", Paint.Stone, 0, 0, -2.0f);
                    StoneWall(L, -5.5f, -1.5f, -2.0f, 2); StoneWall(L, 1.5f, 5.5f, -2.0f, 2);
                    Shrine(L, 0, 0, true, false, Paint.Stone, Paint.Stone);
                    d.start[Ammo.Stone] = 3; d.start[Ammo.Split] = 3; d.enemyAmmo = new[] { Ammo.Split, Ammo.Stone, Ammo.Fire };
                    break;
                case 14:  // 吊秤：帅旗站在吊秤一头的秤盘上 —— 另一头秤盘短时间挨几下（分裂丸），秤杆一翘把帅旗掀飞
                    d.title = "第十五关 · 吊秤"; d.timeOfDay = 4; d.kingHP = 6;
                    d.mechTip = "往另一头秤盘里打\n压翻它掀飞帅旗"; d.mechPiece = "M_ScaleBeam";
                    Wall(L, -5, -1, -3.2f, 1, Paint.Cream, Paint.Teal); Wall(L, 1, 5, -3.2f, 1, Paint.Teal, Paint.Cream);
                    L.Add("M_ScalePost", Paint.Bare, 0, 0, 0);
                    L.Add("M_ScaleBeam", Paint.Bare, 0, 2.0f, 0);
                    L.king = new Vector3(-2.0f, 0.76f, 0);
                    Tower(L, -4.4f, 1.2f, 3, Paint.Sage, Paint.Cream); Tower(L, 4.4f, 1.2f, 3, Paint.Blue, Paint.Cream);
                    d.start[Ammo.Stone] = 4; d.start[Ammo.Split] = 3; d.enemyAmmo = new[] { Ammo.Split, Ammo.Bomb, Ammo.Stone };
                    break;
                // ═══════════ 烟火夜：大混战，最后王城
                case 15:
                    d.title = "第十六关 · 要塞"; d.timeOfDay = 5; d.kingHP = 5;
                    L.Add("S_Shield", Paint.Stone, 0, 0, -4.8f);
                    StoneWall(L, -5, -1, -3.6f, 2); StoneWall(L, 1, 5, -3.6f, 2);
                    Wall(L, -3, 3, -2.2f, 2, Paint.Ochre, Paint.Cream);
                    Tower(L, -4.4f, -2.2f, 3, Paint.Teal, Paint.Cream); Tower(L, 4.4f, -2.2f, 3, Paint.Cream, Paint.Teal);
                    Tower(L, -2.6f, 1.4f, 2, Paint.Sage, Paint.Blue, true, true); Tower(L, 2.6f, 1.4f, 2, Paint.Blue, Paint.Sage, true, true);
                    Keep(L, 0, 2.6f, 4, Paint.Brick, Paint.Teal);
                    Shrine(L, 0, 0, true, true, Paint.Stone, Paint.Stone);
                    d.start[Ammo.Stone] = 4; d.start[Ammo.Fire] = 1; d.start[Ammo.Ice] = 1; d.start[Ammo.Bomb] = 1; d.start[Ammo.Split] = 1;
                    d.enemyAmmo = new[] { Ammo.Fire, Ammo.Bomb, Ammo.Ice, Ammo.Split, Ammo.Stone };
                    d.fireEvery = new Vector2(4f, 5.5f); d.aimError = 0.8f;
                    break;
                case 16:  // 五弹混战：拱桥上的石堡，前面草垛、旁边火药桶、墙头水桶 —— 哪条路都能打
                    d.title = "第十七关 · 混战"; d.timeOfDay = 5; d.kingHP = 6;
                    StoneWall(L, -6, -1.5f, -3.6f, 1); StoneWall(L, 1.5f, 6, -3.6f, 1);
                    for (int k = -2; k <= 2; k++) L.Add("M_Hay", Paint.Ochre, k * 1.1f, 0, -2.4f);
                    Bridge(L, 0, 0);
                    Shrine(L, 0, 0, true, false, Paint.Stone, Paint.Stone, 1.5f);
                    L.Add("M_Keg", Paint.Brick, -3.0f, 0, -0.2f); L.Add("M_Keg", Paint.Brick, 3.0f, 0, -0.2f);
                    L.Add("M_Bucket", Paint.Teal, -4.5f, 0, -2.6f);   // 石墙后面左侧地上（冰丸高抛打它，冻脆左边一片）
                    Tower(L, -4.6f, 1.4f, 3, Paint.Teal, Paint.Cream); Tower(L, 4.6f, 1.4f, 3, Paint.Cream, Paint.Brick);
                    d.start[Ammo.Stone] = 4; d.start[Ammo.Fire] = 1; d.start[Ammo.Ice] = 1; d.start[Ammo.Bomb] = 1; d.start[Ammo.Split] = 1;
                    d.enemyAmmo = new[] { Ammo.Fire, Ammo.Bomb, Ammo.Split, Ammo.Ice, Ammo.Stone };
                    d.fireEvery = new Vector2(4f, 5.2f); d.aimError = 0.75f;
                    break;
                default:  // 第十八关：终局大城，帅旗在两道墙后的石砌小堡里
                    d.title = "第十八关 · 王城"; d.timeOfDay = 5; d.kingHP = 5;
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
            // 敌人盯帅旗的程度：第 3 关 12% → 最后一关 35%（11-25 难度模拟：原来一律 40%，新手 70% 的败局是帅旗下面的楼被打晃、帅旗连带歪倒，帅旗本身一次没挨打）
            d.kingFocus = i < 2 ? 0.1f : Mathf.Lerp(0.12f, 0.35f, (i - 2) / (float)(Count - 3));
            return d;
        }
    }
}

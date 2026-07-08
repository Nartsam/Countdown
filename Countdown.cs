using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Thread = System.Threading.Thread;

namespace CountdownActions
{
    static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint count, INPUT[] inputs, int size);

        const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
        const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
        const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004,
                   MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010,
                   MOUSEEVENTF_VIRTUALDESK = 0x4000, MOUSEEVENTF_ABSOLUTE = 0x8000;
        const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_SCANCODE = 0x0008;

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT { public uint type; public InputUnion U; }

        // 移动到绝对坐标（覆盖多显示器虚拟桌面）并按下/抬起鼠标键
        public static void MouseClickAt(Point p, bool left)
        {
            SetCursorPos(p.X, p.Y);
            int vx = GetSystemMetrics(SM_XVIRTUALSCREEN), vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN), vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            INPUT[] ins = new INPUT[3];
            ins[0].type = INPUT_MOUSE;
            ins[0].U.mi.dx = (int)((p.X - vx) * 65535L / (vw - 1));
            ins[0].U.mi.dy = (int)((p.Y - vy) * 65535L / (vh - 1));
            ins[0].U.mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;
            ins[1].type = INPUT_MOUSE;
            ins[1].U.mi.dwFlags = left ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_RIGHTDOWN;
            ins[2].type = INPUT_MOUSE;
            ins[2].U.mi.dwFlags = left ? MOUSEEVENTF_LEFTUP : MOUSEEVENTF_RIGHTUP;
            SendInput(3, ins, Marshal.SizeOf(typeof(INPUT)));
        }

        static bool IsExtended(Keys k)
        {
            switch (k)
            {
                case Keys.Up: case Keys.Down: case Keys.Left: case Keys.Right:
                case Keys.Insert: case Keys.Delete: case Keys.Home: case Keys.End:
                case Keys.PageUp: case Keys.PageDown:
                case Keys.NumLock: case Keys.Divide: case Keys.PrintScreen:
                case Keys.Apps: case Keys.LWin: case Keys.RWin:
                    return true;
                default: return false;
            }
        }

        // 同时携带虚拟键码和硬件扫描码，用扫描码模式发送，兼容 DirectInput 游戏
        public static void SendKey(Keys key, bool up)
        {
            INPUT[] ins = new INPUT[1];
            ins[0].type = INPUT_KEYBOARD;
            ushort scan = (ushort)MapVirtualKey((uint)key, 0);
            uint flags = up ? KEYEVENTF_KEYUP : 0u;
            if (IsExtended(key)) flags |= KEYEVENTF_EXTENDEDKEY;
            if (scan != 0) flags |= KEYEVENTF_SCANCODE;
            ins[0].U.ki.wVk = (ushort)key;
            ins[0].U.ki.wScan = scan;
            ins[0].U.ki.dwFlags = flags;
            SendInput(1, ins, Marshal.SizeOf(typeof(INPUT)));
        }
    }

    static class Ui
    {
        public static float Scale = 1f;
        public static int S(int v) { return (int)Math.Round(v * Scale); }
    }

    static class Fmt
    {
        public static string Remaining(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            int total = (int)Math.Ceiling(t.TotalSeconds);
            if (total >= 3600) return string.Format("{0}:{1:00}:{2:00}", total / 3600, (total % 3600) / 60, total % 60);
            if (total >= 60) return string.Format("{0}:{1:00}", total / 60, total % 60);
            return total + "s";
        }

        public static GraphicsPath Rounded(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (Control c = new Control())
            using (Graphics g = c.CreateGraphics())
                Ui.Scale = g.DpiX / 96f;
            Application.Run(new MainForm());
        }
    }

    // ---------- 鼠标操作：屏幕上的小圆点 + 倒计时悬浮标签 ----------
    class MouseDot : Form
    {
        public bool IsLeft;
        public TimeSpan Duration;
        public DateTime? Deadline { get; set; }   // 为空表示尚未开始倒计时
        public event EventHandler DeleteRequested;

        public bool Started { get { return Deadline.HasValue; } }

        readonly int D = Ui.S(26);    // 圆点直径
        readonly int Pad = Ui.S(2);
        bool dragging;
        Point dragStart;
        string text = "";
        Font font = new Font("Segoe UI", 9f, FontStyle.Bold);

        public MouseDot(bool isLeft, TimeSpan duration)
        {
            IsLeft = isLeft;
            Duration = duration;
            text = "待 " + Fmt.Remaining(duration);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            Size = new Size(Ui.S(140), D + Pad * 2);
            DoubleBuffered = true;

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem del = new ToolStripMenuItem("删除此操作");
            del.Click += delegate
            {
                EventHandler h = DeleteRequested;
                if (h != null) h(this, EventArgs.Empty);
            };
            menu.Items.Add(del);
            ContextMenuStrip = menu;

            MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) { dragging = true; dragStart = e.Location; }
            };
            MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (dragging)
                    Location = new Point(Location.X + e.X - dragStart.X, Location.Y + e.Y - dragStart.Y);
            };
            MouseUp += delegate { dragging = false; };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        // 圆点中心 = 最终点击的屏幕坐标
        public Point ClickPoint
        {
            get { return new Point(Left + Pad + D / 2, Top + Pad + D / 2); }
        }

        public void Start(DateTime deadline)
        {
            Deadline = deadline;
            Invalidate();
        }

        public void UpdateCountdown(TimeSpan remaining)
        {
            string s = Fmt.Remaining(remaining);
            if (s != text) { text = s; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 左键蓝色，右键橙红色
            Color c = IsLeft ? Color.FromArgb(0, 122, 255) : Color.FromArgb(255, 69, 58);
            using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, Pad, Pad, D, D);
            using (Pen w = new Pen(Color.White, 2f)) g.DrawEllipse(w, Pad, Pad, D, D);

            StringFormat sf = new StringFormat();
            sf.Alignment = StringAlignment.Center;
            sf.LineAlignment = StringAlignment.Center;
            g.DrawString(IsLeft ? "左" : "右", font, Brushes.White, new RectangleF(Pad, Pad, D, D), sf);

            // 旁边的倒计时小胶囊：未开始为灰色，开始后为深色
            SizeF ts = g.MeasureString(text, font);
            RectangleF pill = new RectangleF(Pad + D + 6, Pad + (D - ts.Height - 4) / 2, ts.Width + 12, ts.Height + 4);
            Color pillColor = Started ? Color.FromArgb(210, 30, 30, 34) : Color.FromArgb(210, 105, 105, 110);
            using (GraphicsPath path = Fmt.Rounded(pill, 8))
            using (SolidBrush bg = new SolidBrush(pillColor))
                g.FillPath(bg, path);
            g.DrawString(text, font, Brushes.White, pill.X + 6, pill.Y + 2);
        }
    }

    // ---------- 键盘操作：右上角半透明悬浮列表 ----------
    class KeyItem
    {
        public Keys KeyData;               // 含修饰键的组合键
        public TimeSpan Duration;
        public DateTime? Deadline;         // 为空表示尚未开始倒计时
        public string Remaining = "";
        public string DisplayName;
    }

    class KeyListForm : Form
    {
        public event Action<int> DeleteRow;

        List<KeyItem> items;   // 与 MainForm 共享同一个列表
        readonly int W = Ui.S(230);
        readonly int HeaderH = Ui.S(24);
        readonly int RowH = Ui.S(26);
        Font font = new Font("Segoe UI", 9.5f);
        Font headerFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        bool dragging;
        Point dragStart;
        int menuRow = -1;

        public KeyListForm(List<KeyItem> shared)
        {
            items = shared;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(25, 25, 30);
            Opacity = 0.86;
            DoubleBuffered = true;
            Width = W;

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - W - Ui.S(12), wa.Top + Ui.S(12));

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem del = new ToolStripMenuItem("删除此操作");
            del.Click += delegate
            {
                Action<int> h = DeleteRow;
                if (h != null && menuRow >= 0 && menuRow < items.Count) h(menuRow);
            };
            menu.Items.Add(del);
            menu.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                if (menuRow < 0 || menuRow >= items.Count) e.Cancel = true;
            };
            ContextMenuStrip = menu;

            MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) { dragging = true; dragStart = e.Location; }
                else if (e.Button == MouseButtons.Right)
                    menuRow = (e.Y - HeaderH) >= 0 ? (e.Y - HeaderH) / RowH : -1;
            };
            MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (dragging)
                    Location = new Point(Location.X + e.X - dragStart.X, Location.Y + e.Y - dragStart.Y);
            };
            MouseUp += delegate { dragging = false; };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public void SyncLayout()
        {
            if (items.Count == 0) { Hide(); return; }
            Height = HeaderH + items.Count * RowH + Ui.S(4);
            if (!Visible) Show();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush hb = new SolidBrush(Color.FromArgb(50, 50, 60)))
                g.FillRectangle(hb, 0, 0, Width, HeaderH);
            TextRenderer.DrawText(g, "按键倒计时（拖动移动）", headerFont,
                new Rectangle(0, 0, Width, HeaderH), Color.Gainsboro,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            for (int i = 0; i < items.Count; i++)
            {
                int y = HeaderH + i * RowH;
                bool started = items[i].Deadline.HasValue;
                TextRenderer.DrawText(g, items[i].DisplayName, font,
                    new Rectangle(Ui.S(10), y, W - Ui.S(84), RowH), Color.White,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, items[i].Remaining, font,
                    new Rectangle(W - Ui.S(82), y, Ui.S(72), RowH),
                    started ? Color.FromArgb(120, 220, 130) : Color.Silver,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                if (i > 0)
                    using (Pen p = new Pen(Color.FromArgb(60, 255, 255, 255)))
                        g.DrawLine(p, Ui.S(6), y, Width - Ui.S(6), y);
            }
        }
    }

    // ---------- 主窗口 ----------
    class MainForm : Form
    {
        NumericUpDown numH, numM, numS;
        TextBox txtKey;
        CheckBox chkMin;
        Keys capturedKeyData = Keys.None;

        List<MouseDot> dots = new List<MouseDot>();
        List<KeyItem> keyItems = new List<KeyItem>();
        KeyListForm keyList;
        Timer timer;

        public MainForm()
        {
            Text = "倒计时点击器";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Ui.S(340), Ui.S(236));
            Font = new Font("Microsoft YaHei UI", 9f);

            Label lb1 = new Label();
            lb1.Text = "倒计时：";
            lb1.Location = new Point(Ui.S(14), Ui.S(19));
            lb1.AutoSize = true;
            Controls.Add(lb1);

            numH = MakeNum(78, 16, 99, 0);
            AddUnitLabel("时", 130, 19);
            numM = MakeNum(158, 16, 59, 0);
            AddUnitLabel("分", 210, 19);
            numS = MakeNum(238, 16, 59, 10);
            AddUnitLabel("秒", 290, 19);

            Button btnLeft = new Button();
            btnLeft.Text = "＋ 鼠标左键";
            btnLeft.Location = new Point(Ui.S(14), Ui.S(52));
            btnLeft.Size = new Size(Ui.S(150), Ui.S(32));
            btnLeft.Click += delegate { AddMouse(true); };
            Controls.Add(btnLeft);

            Button btnRight = new Button();
            btnRight.Text = "＋ 鼠标右键";
            btnRight.Location = new Point(Ui.S(176), Ui.S(52));
            btnRight.Size = new Size(Ui.S(150), Ui.S(32));
            btnRight.Click += delegate { AddMouse(false); };
            Controls.Add(btnRight);

            Label lb2 = new Label();
            lb2.Text = "键盘按键：";
            lb2.Location = new Point(Ui.S(14), Ui.S(100));
            lb2.AutoSize = true;
            Controls.Add(lb2);

            txtKey = new TextBox();
            txtKey.ReadOnly = true;
            txtKey.Text = "点此处再按键";
            txtKey.Location = new Point(Ui.S(90), Ui.S(96));
            txtKey.Width = Ui.S(130);
            txtKey.TextAlign = HorizontalAlignment.Center;
            txtKey.PreviewKeyDown += delegate(object s, PreviewKeyDownEventArgs e) { e.IsInputKey = true; };
            txtKey.KeyDown += delegate(object s, KeyEventArgs e)
            {
                capturedKeyData = e.KeyData;
                txtKey.Text = ComboName(capturedKeyData);
                e.Handled = true;
                e.SuppressKeyPress = true;
            };
            Controls.Add(txtKey);

            Button btnKey = new Button();
            btnKey.Text = "＋ 添加";
            btnKey.Location = new Point(Ui.S(230), Ui.S(94));
            btnKey.Size = new Size(Ui.S(96), Ui.S(27));
            btnKey.Click += delegate { AddKey(); };
            Controls.Add(btnKey);

            chkMin = new CheckBox();
            chkMin.Text = "倒计时开始后最小化本窗口";
            chkMin.Checked = true;
            chkMin.AutoSize = true;
            chkMin.Location = new Point(Ui.S(14), Ui.S(132));
            Controls.Add(chkMin);

            Button btnStart = new Button();
            btnStart.Text = "▶ 开始倒计时";
            btnStart.Location = new Point(Ui.S(14), Ui.S(160));
            btnStart.Size = new Size(Ui.S(312), Ui.S(38));
            btnStart.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
            btnStart.Click += delegate { StartAll(); };
            Controls.Add(btnStart);

            Label hint = new Label();
            hint.Text = "拖动圆点 / 列表可调整位置，右键单击可删除操作。";
            hint.ForeColor = Color.Gray;
            hint.AutoSize = true;
            hint.Location = new Point(Ui.S(14), Ui.S(210));
            Controls.Add(hint);

            timer = new Timer();
            timer.Interval = 100;
            timer.Tick += delegate { OnTick(); };
            timer.Start();
        }

        NumericUpDown MakeNum(int x, int y, int max, int val)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = 0;
            n.Maximum = max;
            n.Value = val;
            n.Location = new Point(Ui.S(x), Ui.S(y));
            n.Width = Ui.S(48);
            n.TextAlign = HorizontalAlignment.Center;
            Controls.Add(n);
            return n;
        }

        void AddUnitLabel(string text, int x, int y)
        {
            Label lb = new Label();
            lb.Text = text;
            lb.Location = new Point(Ui.S(x), Ui.S(y));
            lb.AutoSize = true;
            Controls.Add(lb);
        }

        TimeSpan GetDuration()
        {
            return new TimeSpan((int)numH.Value, (int)numM.Value, (int)numS.Value);
        }

        static string KeyName(Keys k)
        {
            switch (k)
            {
                case Keys.ControlKey: return "Ctrl";
                case Keys.ShiftKey: return "Shift";
                case Keys.Menu: return "Alt";
                case Keys.Return: return "Enter";
                case Keys.Next: return "PageDown";
                case Keys.Prior: return "PageUp";
                case Keys.Capital: return "CapsLock";
                default: return k.ToString();
            }
        }

        static string ComboName(Keys keyData)
        {
            List<string> parts = new List<string>();
            if ((keyData & Keys.Control) != 0) parts.Add("Ctrl");
            if ((keyData & Keys.Shift) != 0) parts.Add("Shift");
            if ((keyData & Keys.Alt) != 0) parts.Add("Alt");
            Keys code = keyData & Keys.KeyCode;
            if (code != Keys.None && code != Keys.ControlKey && code != Keys.ShiftKey && code != Keys.Menu)
                parts.Add(KeyName(code));
            return parts.Count > 0 ? string.Join("+", parts.ToArray()) : "";
        }

        void AddMouse(bool isLeft)
        {
            TimeSpan dur = GetDuration();
            if (dur <= TimeSpan.Zero)
            {
                MessageBox.Show(this, "倒计时时长必须大于 0。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            MouseDot dot = new MouseDot(isLeft, dur);
            Rectangle b = Screen.PrimaryScreen.Bounds;
            int offset = (dots.Count % 8) * Ui.S(34);
            dot.Location = new Point(b.Left + b.Width / 2 - Ui.S(15) + offset, b.Top + b.Height / 2 - Ui.S(15) + offset);
            dot.DeleteRequested += delegate(object s, EventArgs e)
            {
                MouseDot d = (MouseDot)s;
                dots.Remove(d);
                d.Close();
                d.Dispose();
            };
            dots.Add(dot);
            dot.Show();
        }

        void AddKey()
        {
            TimeSpan dur = GetDuration();
            if (dur <= TimeSpan.Zero)
            {
                MessageBox.Show(this, "倒计时时长必须大于 0。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (capturedKeyData == Keys.None)
            {
                MessageBox.Show(this, "请先点击文本框，然后按下要模拟的按键（可带 Ctrl/Shift/Alt）。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            KeyItem item = new KeyItem();
            item.KeyData = capturedKeyData;
            item.Duration = dur;
            item.Remaining = "待 " + Fmt.Remaining(dur);
            item.DisplayName = ComboName(capturedKeyData);
            keyItems.Add(item);

            if (keyList == null || keyList.IsDisposed)
            {
                keyList = new KeyListForm(keyItems);
                keyList.DeleteRow += delegate(int idx)
                {
                    if (idx >= 0 && idx < keyItems.Count) keyItems.RemoveAt(idx);
                    keyList.SyncLayout();
                };
            }
            keyList.SyncLayout();
        }

        void StartAll()
        {
            if (dots.Count == 0 && keyItems.Count == 0)
            {
                MessageBox.Show(this, "请先添加至少一个操作。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DateTime now = DateTime.Now;
            bool startedAny = false;
            foreach (MouseDot d in dots)
                if (!d.Started) { d.Start(now + d.Duration); startedAny = true; }
            foreach (KeyItem k in keyItems)
                if (!k.Deadline.HasValue) { k.Deadline = now + k.Duration; startedAny = true; }
            if (keyList != null && !keyList.IsDisposed) keyList.Invalidate();
            if (startedAny && chkMin.Checked) WindowState = FormWindowState.Minimized;
        }

        void OnTick()
        {
            DateTime now = DateTime.Now;

            for (int i = dots.Count - 1; i >= 0; i--)
            {
                MouseDot d = dots[i];
                if (!d.Started) continue;
                TimeSpan rem = d.Deadline.Value - now;
                if (rem <= TimeSpan.Zero)
                {
                    dots.RemoveAt(i);
                    FireMouse(d);       // 触发后圆点即从屏幕消失
                }
                else d.UpdateCountdown(rem);
            }

            bool removed = false;
            for (int i = keyItems.Count - 1; i >= 0; i--)
            {
                KeyItem k = keyItems[i];
                if (!k.Deadline.HasValue) continue;
                TimeSpan rem = k.Deadline.Value - now;
                if (rem <= TimeSpan.Zero)
                {
                    keyItems.RemoveAt(i);
                    removed = true;
                    FireKey(k);
                }
                else k.Remaining = Fmt.Remaining(rem);
            }
            if (keyList != null && !keyList.IsDisposed)
            {
                if (removed) keyList.SyncLayout();
                else if (keyList.Visible) keyList.Invalidate();
            }
        }

        static void FireMouse(MouseDot d)
        {
            Point p = d.ClickPoint;
            bool isLeft = d.IsLeft;
            d.Hide();
            d.Close();
            d.Dispose();
            Native.MouseClickAt(p, isLeft);
        }

        static void FireKey(KeyItem k)
        {
            List<Keys> mods = new List<Keys>();
            if ((k.KeyData & Keys.Control) != 0) mods.Add(Keys.ControlKey);
            if ((k.KeyData & Keys.Shift) != 0) mods.Add(Keys.ShiftKey);
            if ((k.KeyData & Keys.Alt) != 0) mods.Add(Keys.Menu);
            Keys code = k.KeyData & Keys.KeyCode;
            bool codeIsModifier = code == Keys.ControlKey || code == Keys.ShiftKey || code == Keys.Menu;

            foreach (Keys m in mods) { Native.SendKey(m, false); Thread.Sleep(15); }
            if (!codeIsModifier && code != Keys.None)
            {
                Native.SendKey(code, false);
                Thread.Sleep(15);
                Native.SendKey(code, true);
                Thread.Sleep(15);
            }
            for (int i = mods.Count - 1; i >= 0; i--) { Native.SendKey(mods[i], true); Thread.Sleep(15); }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            foreach (MouseDot d in dots) { d.Close(); d.Dispose(); }
            dots.Clear();
            if (keyList != null && !keyList.IsDisposed) keyList.Close();
            base.OnFormClosed(e);
        }
    }
}

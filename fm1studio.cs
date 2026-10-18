// fm1studio: Forza Motorsport 1 (original Xbox) model and texture viewer / OBJ extractor, working like Forza Studio 4.6:
// File > Import opens one file at a time (.cab / .ca2 archive, or a loose .carbin / .xpr).
//   Models tab:   carbin -> LOD -> part -> mesh (one per material), checkboxes; the view draws the checked meshes;
//                 right-click: Extract Checked / Extract All as Wavefront OBJ.
//   Textures tab: the .xpr files; select one to see it; right-click: Save As / Extract Checked / Extract All (PNG).
// Names: part = the name stored in the carbin; mesh = the Turn10 name of the subpart's material number
// (default.xbe enum, see fm1_names.md). The OBJ uses those names only (g = part, usemtl = material).
//
//   fm1studio [file]
//
// Build: csc /nologo /platform:x86 /optimize /target:winexe /main:Fm1Studio /win32icon:fm1studio.ico
//            /r:System.Drawing.dll /r:System.Windows.Forms.dll
//            /r:C:\Windows\assembly\GAC_32\Microsoft.Xna.Framework\3.1.0.0__6d5c3888ef60e27d\Microsoft.Xna.Framework.dll
//            fm1studio.cs fm1model.cs fm1xpr.cs fm1cab.cs
// Needs fm1studio.exe.config (XNA 3.1 is a .NET 2 assembly): useLegacyV2RuntimeActivationPolicy="true".
//
// Axes: FM1 z is negated (front at -Z, right-handed, like FM2/FM4); the OBJ uses the same frame.
// Mouse: left drag = orbit, right or middle drag = pan, wheel = zoom. Double-click a node = frame it.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using XColor = Microsoft.Xna.Framework.Graphics.Color;
using DColor = System.Drawing.Color;
using DRectangle = System.Drawing.Rectangle;

// Colours of the FM1 menus (car select screen): black bars, orange stripes, light grey showroom, yellow selection.
static class Fm1Theme
{
    public static readonly DColor Black = DColor.FromArgb(8, 8, 8);
    public static readonly DColor Bar = DColor.FromArgb(18, 18, 18);
    public static readonly DColor Panel = DColor.FromArgb(28, 28, 28);
    public static readonly DColor Orange = DColor.FromArgb(240, 138, 0);
    public static readonly DColor Yellow = DColor.FromArgb(232, 210, 26);
    public static readonly DColor Blue = DColor.FromArgb(40, 110, 200);
    public static readonly DColor LightBlue = DColor.FromArgb(150, 200, 240);
    public static readonly DColor Green = DColor.FromArgb(70, 180, 60);
    public static readonly DColor Red = DColor.FromArgb(220, 60, 40);
    public static readonly DColor Silver = DColor.FromArgb(232, 232, 232);
    public static readonly DColor Text = DColor.White;
    public static readonly DColor Dim = DColor.FromArgb(130, 130, 130);
    public static readonly Font Bold = new Font("Tahoma", 9f, FontStyle.Bold);
    public static readonly Font Regular = new Font("Tahoma", 9f);
}

class Fm1ColorTable : ProfessionalColorTable
{
    static readonly DColor Hover = DColor.FromArgb(60, 60, 60);
    public override DColor MenuStripGradientBegin { get { return Fm1Theme.Black; } }
    public override DColor MenuStripGradientEnd { get { return Fm1Theme.Black; } }
    public override DColor ToolStripDropDownBackground { get { return Fm1Theme.Panel; } }
    public override DColor ImageMarginGradientBegin { get { return Fm1Theme.Panel; } }
    public override DColor ImageMarginGradientMiddle { get { return Fm1Theme.Panel; } }
    public override DColor ImageMarginGradientEnd { get { return Fm1Theme.Panel; } }
    public override DColor MenuBorder { get { return Fm1Theme.Orange; } }
    public override DColor MenuItemBorder { get { return Fm1Theme.Yellow; } }
    public override DColor MenuItemSelected { get { return Hover; } }
    public override DColor MenuItemSelectedGradientBegin { get { return Hover; } }
    public override DColor MenuItemSelectedGradientEnd { get { return Hover; } }
    public override DColor MenuItemPressedGradientBegin { get { return Fm1Theme.Panel; } }
    public override DColor MenuItemPressedGradientEnd { get { return Fm1Theme.Panel; } }
    public override DColor CheckBackground { get { return Fm1Theme.Blue; } }
    public override DColor CheckSelectedBackground { get { return Fm1Theme.Blue; } }
    public override DColor StatusStripGradientBegin { get { return Fm1Theme.Black; } }
    public override DColor StatusStripGradientEnd { get { return Fm1Theme.Black; } }
    public override DColor SeparatorDark { get { return DColor.FromArgb(70, 70, 70); } }
    public override DColor SeparatorLight { get { return Fm1Theme.Panel; } }
}

class Fm1Renderer : ToolStripProfessionalRenderer
{
    public Fm1Renderer() : base(new Fm1ColorTable()) { RoundedEdges = false; }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? (e.Item.Tag as string == "hint" ? e.Item.ForeColor : Fm1Theme.Text) : Fm1Theme.Dim;
        base.OnRenderItemText(e);
    }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Fm1Theme.Text; base.OnRenderArrow(e); }
}

// The Models / Textures buttons: black bar, active one with white text and an orange underline.
class Fm1TabRenderer : ToolStripRenderer
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(Fm1Theme.Bar); }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        var b = (ToolStripButton)e.Item;
        var r = new DRectangle(System.Drawing.Point.Empty, e.Item.Size);
        using (var bg = new SolidBrush(b.Checked ? Fm1Theme.Black : Fm1Theme.Bar)) e.Graphics.FillRectangle(bg, r);
        if (b.Checked) using (var o = new SolidBrush(Fm1Theme.Orange)) e.Graphics.FillRectangle(o, 0, r.Height - 4, r.Width, 4);
        else if (b.Selected) using (var y = new Pen(Fm1Theme.Yellow)) e.Graphics.DrawRectangle(y, 0, 0, r.Width - 1, r.Height - 1);
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = ((ToolStripButton)e.Item).Checked ? Fm1Theme.Text : Fm1Theme.Dim;
        base.OnRenderItemText(e);
    }
}

// Panel the GPU draws into: no background erase.
class ViewPanel : Panel
{
    public ViewPanel() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true); }
}

// A carbin of the imported file.
class ModelInfo
{
    public string Path;                 // path inside the archive, or the file path
    public Fm1Car Car;
    public string Livery;               // texture key of the car's livery0hi, or null
    public DColor Paint = DColor.FromArgb(140, 140, 140);
}

// A mesh node: one subpart of one part (Forza Studio's "section_subsection").
class MeshTag
{
    public ModelInfo Model;
    public Fm1Part Part;
    public Fm1Subpart Sub;
    public VertexPositionNormalTexture[] Verts;   // built on first draw
    public string Name { get { return Model.Car != null ? (Part.Name ?? "Part") : ""; } }
    public string Material { get { return Fm1Materials.Name(Sub.Material); } }
}

class TextureTag
{
    public string Path;
    public Func<byte[]> Read;
}

class StudioForm : Form
{
    Panel tabs = new Panel();
    ToolStripButton btnModels = new ToolStripButton("Models");
    ToolStripButton btnTextures = new ToolStripButton("Textures");
    TreeView tvModels = new TreeView();
    TreeView tvTextures = new TreeView();
    ViewPanel view = new ViewPanel();
    PictureBox picture = new PictureBox();
    ToolStripStatusLabel status = new ToolStripStatusLabel();
    ToolStripMenuItem mnuWireframe = new ToolStripMenuItem("Show Wireframe");
    ToolStripMenuItem mnuTextures = new ToolStripMenuItem("Show Textures");

    GraphicsDevice device;
    BasicEffect effect, lineEffect;
    VertexDeclaration vdecl, ldecl;
    bool graphicsFailed;

    string importedPath;
    readonly List<ModelInfo> models = new List<ModelInfo>();
    readonly Dictionary<string, int[]> texPixels = new Dictionary<string, int[]>();
    readonly Dictionary<string, Size> texSize = new Dictionary<string, Size>();
    readonly Dictionary<string, Texture2D> gpuTex = new Dictionary<string, Texture2D>();
    MeshTag selectedMesh;
    TreeNode selectedNode;

    // orbit camera
    Vector3 target = Vector3.Zero;
    float yaw = -2.3f, pitch = 0.3f, distance = 6f;
    System.Drawing.Point lastMouse;
    MouseButtons dragButton = MouseButtons.None;

    public StudioForm(string initial)
    {
        Text = "FM1 Studio";
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }   // fm1studio.ico, built in with /win32icon
        Width = 1280;
        Height = 820;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Fm1Theme.Black;
        Font = Fm1Theme.Regular;
        var renderer = new Fm1Renderer();

        var menu = new MenuStrip { Renderer = renderer, Font = Fm1Theme.Bold };
        var file = new ToolStripMenuItem("File");
        file.DropDownItems.Add("Import...", null, delegate { ImportDialog(); });
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Exit", null, delegate { Close(); });
        menu.Items.Add(file);

        var statusStrip = new StatusStrip { Renderer = renderer, Font = Fm1Theme.Bold, SizingGrip = false };
        status.Spring = true;
        status.TextAlign = ContentAlignment.MiddleLeft;
        statusStrip.Items.Add(status);
        AddHint(statusStrip, Fm1Theme.Green, "Right-click  Extract");
        AddHint(statusStrip, Fm1Theme.Yellow, "Left drag  Orbit");
        AddHint(statusStrip, Fm1Theme.LightBlue, "Right drag  Pan");
        AddHint(statusStrip, Fm1Theme.Red, "Wheel  Zoom");

        foreach (var tv in new[] { tvModels, tvTextures })
        {
            tv.Dock = DockStyle.Fill;
            tv.HideSelection = false;
            tv.BorderStyle = BorderStyle.None;
            tv.BackColor = Fm1Theme.Panel;
            tv.ForeColor = Fm1Theme.Text;
            tv.LineColor = DColor.FromArgb(90, 90, 90);
            tv.ItemHeight = 20;
            tv.CheckBoxes = true;
            tv.DrawMode = TreeViewDrawMode.OwnerDrawText;
            tv.DrawNode += OnDrawNode;
        }
        tvModels.AfterCheck += OnModelsAfterCheck;
        tvModels.AfterSelect += delegate(object s, TreeViewEventArgs e) { selectedNode = e.Node; selectedMesh = e.Node.Tag as MeshTag; RedrawView(); };
        tvModels.NodeMouseDoubleClick += delegate(object s, TreeNodeMouseClickEventArgs e) { FrameNode(e.Node); RedrawView(); };
        tvTextures.AfterSelect += delegate(object s, TreeViewEventArgs e) { ShowTexture(e.Node.Tag as TextureTag); };
        tvTextures.AfterCheck += OnTexturesAfterCheck;

        var ctxModels = new ContextMenuStrip { Renderer = renderer, Font = Fm1Theme.Bold };
        ctxModels.Items.Add("Extract Checked", null, delegate { ExportObj(true); });
        ctxModels.Items.Add("Extract All", null, delegate { ExportObj(false); });
        tvModels.ContextMenuStrip = ctxModels;
        var ctxTextures = new ContextMenuStrip { Renderer = renderer, Font = Fm1Theme.Bold };
        ctxTextures.Items.Add("Save As...", null, delegate { SaveTextureAs(); });
        ctxTextures.Items.Add("Extract Checked", null, delegate { ExtractTextures(true); });
        ctxTextures.Items.Add("Extract All", null, delegate { ExtractTextures(false); });
        tvTextures.ContextMenuStrip = ctxTextures;

        // Models / Textures switch (Forza Studio's two tabs), drawn as FM1 tabs: black, orange underline when active.
        var tabStrip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Renderer = new Fm1TabRenderer(), Font = Fm1Theme.Bold, Dock = DockStyle.Top, Padding = new Padding(0) };
        foreach (var b in new[] { btnModels, btnTextures })
        {
            b.AutoSize = false;
            b.Size = new Size(110, 28);
            tabStrip.Items.Add(b);
        }
        btnModels.Click += delegate { ShowTab(0); };
        btnTextures.Click += delegate { ShowTab(1); };
        tabs.Dock = DockStyle.Fill;
        tabs.BackColor = Fm1Theme.Panel;
        tabs.Controls.Add(tvModels);
        tabs.Controls.Add(tvTextures);
        tabs.Controls.Add(tabStrip);

        view.Dock = DockStyle.Fill;
        view.Paint += delegate { Render(); };
        view.Resize += delegate { ResetDevice(); };
        view.MouseDown += delegate(object s, MouseEventArgs e) { dragButton = e.Button; lastMouse = e.Location; view.Focus(); };
        view.MouseUp += delegate { dragButton = MouseButtons.None; };
        view.MouseMove += OnViewMouseMove;
        view.MouseWheel += delegate(object s, MouseEventArgs e) { distance *= e.Delta > 0 ? 0.88f : 1.14f; distance = Math.Max(0.05f, distance); RedrawView(); };
        var ctxView = new ContextMenuStrip { Renderer = renderer, Font = Fm1Theme.Bold };
        mnuWireframe.CheckOnClick = true;
        mnuWireframe.CheckedChanged += delegate { RedrawView(); };
        mnuTextures.CheckOnClick = true;
        mnuTextures.Checked = true;
        mnuTextures.CheckedChanged += delegate { RedrawView(); };
        ctxView.Items.Add(mnuWireframe);
        ctxView.Items.Add(mnuTextures);
        ctxView.Items.Add("Reset View", null, delegate { FrameChecked(); RedrawView(); });
        view.MouseUp += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Right && !panned) ctxView.Show(view, e.Location); panned = false; };

        picture.Dock = DockStyle.Fill;
        picture.SizeMode = PictureBoxSizeMode.Zoom;
        picture.BackColor = Fm1Theme.Silver;
        picture.Visible = false;

        var right = new Panel { Dock = DockStyle.Fill, BackColor = Fm1Theme.Black };
        right.Controls.Add(view);
        right.Controls.Add(picture);
        var main = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, BackColor = Fm1Theme.Black, SplitterWidth = 3 };
        main.Panel1.Controls.Add(tabs);
        main.Panel2.Controls.Add(right);

        Controls.Add(main);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 3, BackColor = Fm1Theme.Orange });
        Controls.Add(menu);
        Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 3, BackColor = Fm1Theme.Orange });
        Controls.Add(statusStrip);
        MainMenuStrip = menu;

        Load += delegate
        {
            main.SplitterDistance = 320;
            InitGraphics();
            ShowTab(0);
            if (initial != null && File.Exists(initial)) Import(initial);
            else UpdateStatus();
        };
        FormClosed += delegate { DisposeGraphics(); };
    }

    bool panned;

    static void AddHint(StatusStrip s, DColor dot, string text)
    {
        s.Items.Add(new ToolStripStatusLabel("\u25CF") { ForeColor = dot, Tag = "hint", Font = new Font("Tahoma", 11f, FontStyle.Bold), Margin = new Padding(14, 0, 0, 0) });
        s.Items.Add(new ToolStripStatusLabel(text));
    }

    void ShowTab(int index)
    {
        bool m = index == 0;
        btnModels.Checked = m;
        btnTextures.Checked = !m;
        tvModels.Visible = m;
        tvTextures.Visible = !m;
        view.Visible = m;
        picture.Visible = !m;
        RedrawView();
    }

    // FM1 selection box: yellow frame around the selected node.
    void OnDrawNode(object sender, DrawTreeNodeEventArgs e)
    {
        if (e.Bounds.Width == 0) return;
        var tv = (TreeView)sender;
        bool sel = (e.State & TreeNodeStates.Selected) != 0;
        var r = new DRectangle(e.Bounds.X, e.Bounds.Y, TextRenderer.MeasureText(e.Node.Text, Fm1Theme.Regular).Width + 6, e.Bounds.Height);
        using (var bg = new SolidBrush(sel ? DColor.FromArgb(58, 58, 58) : tv.BackColor)) e.Graphics.FillRectangle(bg, r);
        if (sel) using (var pen = new Pen(Fm1Theme.Yellow, 2)) e.Graphics.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
        TextRenderer.DrawText(e.Graphics, e.Node.Text, Fm1Theme.Regular, r, Fm1Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
    }

    // ---------------- import ----------------

    void ImportDialog()
    {
        using (var dlg = new OpenFileDialog
        {
            Filter = "All FM1 files|*.cab;*.ca2;*.carbin;*.xpr|FM1 archive (.cab, .ca2)|*.cab;*.ca2|FM1 model (.carbin)|*.carbin|FM1 texture (.xpr)|*.xpr",
            InitialDirectory = importedPath != null ? Path.GetDirectoryName(importedPath) : (Directory.Exists(@"F:\Xenia\Games\FM1_Extracted\media\cabs") ? @"F:\Xenia\Games\FM1_Extracted\media\cabs" : "")
        })
            if (dlg.ShowDialog(this) == DialogResult.OK) Import(dlg.FileName);
    }

    // One file at a time: the previous import is cleared.
    void Import(string path)
    {
        Cursor = Cursors.WaitCursor;
        try
        {
            ClearAll();
            importedPath = path;
            Text = "FM1 Studio - " + Path.GetFileName(path);
            tvModels.BeginUpdate();
            tvTextures.BeginUpdate();
            string lower = path.ToLowerInvariant();
            if (lower.EndsWith(".cab") || lower.EndsWith(".ca2"))
            {
                var cab = Fm1Cab.Load(path);
                foreach (var e in cab.Entries.OrderBy(x => cab.FullPath(x), StringComparer.OrdinalIgnoreCase))
                {
                    string full = cab.FullPath(e);
                    var entry = e;
                    if (full.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase))
                    {
                        string folder = Path.GetDirectoryName(full);
                        AddModel(full, cab.Payload(e), rel => ReadEntry(cab, folder + "\\" + rel));
                    }
                    else if (full.EndsWith(".xpr", StringComparison.OrdinalIgnoreCase))
                        AddTextureNode(full, () => cab.Payload(entry));
                }
            }
            else if (lower.EndsWith(".carbin"))
            {
                string dir = Path.GetDirectoryName(path);
                AddModel(Path.GetFileName(path), File.ReadAllBytes(path), rel => { string f = Path.Combine(dir, rel); return File.Exists(f) ? File.ReadAllBytes(f) : null; });
            }
            else if (lower.EndsWith(".xpr")) AddTextureNode(Path.GetFileName(path), () => File.ReadAllBytes(path));
            tvModels.EndUpdate();
            tvTextures.EndUpdate();
            // A check state set inside BeginUpdate is lost by the native tree: LOD1 of the first model is checked here.
            if (pendingCheck != null) { pendingCheck.Checked = true; pendingCheck = null; }
            ShowTab(tvModels.Nodes.Count == 0 && tvTextures.Nodes.Count > 0 ? 1 : 0);
            FrameChecked();
        }
        catch (Exception ex)
        {
            tvModels.EndUpdate();
            tvTextures.EndUpdate();
            MessageBox.Show(this, string.Format("Failed to import {0}\n\n{1}", path, ex.Message), "FM1 Studio", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { Cursor = Cursors.Default; }
        UpdateStatus();
        RedrawView();
    }

    static byte[] ReadEntry(Fm1Cab cab, string fullPath)
    {
        var e = cab.Entries.FirstOrDefault(x => string.Equals(cab.FullPath(x), fullPath, StringComparison.OrdinalIgnoreCase));
        return e == null ? null : cab.Payload(e);
    }

    void ClearAll()
    {
        tvModels.Nodes.Clear();
        tvTextures.Nodes.Clear();
        models.Clear();
        selectedMesh = null;
        selectedNode = null;
        foreach (var t in gpuTex.Values) t.Dispose();
        gpuTex.Clear();
        texPixels.Clear();
        texSize.Clear();
        if (picture.Image != null) { picture.Image.Dispose(); picture.Image = null; }
    }

    // carbin -> LOD n -> part -> mesh (material), like Forza Studio 4.6. LOD 1 is checked.
    void AddModel(string path, byte[] data, Func<string, byte[]> readNearby)
    {
        Fm1Car car;
        try { car = Fm1Car.Parse(data); }
        catch (Exception ex) { var bad = tvModels.Nodes.Add(path + "  (" + ex.Message + ")"); bad.ForeColor = Fm1Theme.Red; return; }
        var info = new ModelInfo { Path = path, Car = car };
        models.Add(info);
        if (path.EndsWith("car.carbin", StringComparison.OrdinalIgnoreCase)) LoadLivery(info, readNearby);
        var root = tvModels.Nodes.Add(path);
        foreach (int lod in car.Parts.SelectMany(p => p.Subparts).Select(s => s.Lod).Distinct().OrderBy(l => l))
        {
            var lodNode = root.Nodes.Add("LOD" + lod);
            foreach (var part in car.Parts)
            {
                var subs = part.Subparts.Where(s => s.Lod == lod).ToList();
                if (subs.Count == 0) continue;
                var partNode = lodNode.Nodes.Add(part.Name ?? "Part");
                foreach (var sp in subs)
                {
                    var tag = new MeshTag { Model = info, Part = part, Sub = sp };
                    partNode.Nodes.Add(new TreeNode(tag.Material) { Tag = tag });
                }
            }
            if (lod == 1 && models.Count == 1) pendingCheck = lodNode;     // checked after EndUpdate (see Import)
        }
        root.Expand();
    }

    TreeNode pendingCheck;

    static void SetChecked(TreeNode node, bool value)
    {
        foreach (TreeNode n in node.Nodes) { n.Checked = value; SetChecked(n, value); }
    }

    // livery0hi = clean 2D atlas; the .livery override colour fills the transparent (paint) areas of the Body texels.
    void LoadLivery(ModelInfo info, Func<string, byte[]> read)
    {
        byte[] x = read("liveries\\standard\\livery0hi.xpr");
        if (x == null) return;
        string key = info.Path + "|livery0hi";
        AddTexturePixels(key, XprTexture.Parse(x));
        byte[] lv = read("liveries\\standard\\livery0.livery");
        if (lv != null)
        {
            var m = Regex.Match(Encoding.ASCII.GetString(lv), "override\\s+red=(\\d+)\\s+green=(\\d+)\\s+blue=(\\d+)");
            if (m.Success) info.Paint = DColor.FromArgb(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
        }
        // CarbonFiber: carmedia\carbonfibervolume.xpr (named in default.xbe) is not in the archives: dark grey under it.
        FillUnder(key, key + "+paint", info.Paint);
        FillUnder(key, key + "+carbon", DColor.FromArgb(38, 38, 42));
        info.Livery = key;
    }

    void AddTexturePixels(string key, XprTexture t)
    {
        int w, h;
        int[] px = t.DecodeTop(out w, out h);
        if (t.Depth > 1) { w /= t.Depth; var s0 = new int[w * h]; for (int y = 0; y < h; y++) Array.Copy(px, y * w * t.Depth, s0, y * w, w); px = s0; }
        texPixels[key] = px;
        texSize[key] = new Size(w, h);
    }

    void FillUnder(string src, string dst, DColor under)
    {
        int[] px = texPixels[src];
        var outp = new int[px.Length];
        int pr = under.R, pg = under.G, pb = under.B;
        for (int i = 0; i < px.Length; i++)
        {
            int c = px[i], a = (c >> 24) & 255;
            int r = (((c >> 16) & 255) * a + pr * (255 - a)) / 255, g = (((c >> 8) & 255) * a + pg * (255 - a)) / 255, b = ((c & 255) * a + pb * (255 - a)) / 255;
            outp[i] = unchecked((int)0xFF000000) | r << 16 | g << 8 | b;
        }
        texPixels[dst] = outp;
        texSize[dst] = texSize[src];
    }

    void AddTextureNode(string path, Func<byte[]> read)
    {
        tvTextures.Nodes.Add(new TreeNode(path) { Tag = new TextureTag { Path = path, Read = read } });
    }

    // ---------------- models tab ----------------

    bool insideCheck;
    void OnModelsAfterCheck(object sender, TreeViewEventArgs e)
    {
        if (insideCheck) return;
        insideCheck = true;
        SetChecked(e.Node, e.Node.Checked);
        insideCheck = false;
        UpdateStatus();
        RedrawView();
    }

    void OnTexturesAfterCheck(object sender, TreeViewEventArgs e) { }

    static IEnumerable<MeshTag> Meshes(TreeNodeCollection nodes, bool checkedOnly)
    {
        foreach (TreeNode n in nodes)
        {
            var m = n.Tag as MeshTag;
            if (m != null && (!checkedOnly || n.Checked)) yield return m;
            foreach (var c in Meshes(n.Nodes, checkedOnly)) yield return c;
        }
    }

    void UpdateStatus()
    {
        if (importedPath == null) { status.Text = "File > Import an FM1 .cab, .ca2, .carbin or .xpr"; return; }
        var ch = Meshes(tvModels.Nodes, true).ToList();
        status.Text = string.Format("{0}   {1} model(s), {2} texture(s)   checked: {3} meshes, {4:N0} vertices, {5:N0} faces", Path.GetFileName(importedPath),
            models.Count, tvTextures.Nodes.Count, ch.Count, ch.Select(m => m.Part).Distinct().Sum(p => p.Count), ch.Sum(m => Fm1Car.Triangles(m.Sub.Strip, false).Count()));
    }

    // ---------------- OBJ export ----------------

    void ExportObj(bool checkedOnly)
    {
        var meshes = Meshes(tvModels.Nodes, checkedOnly).ToList();
        if (meshes.Count == 0) { MessageBox.Show(this, "No meshes to extract.", "FM1 Studio"); return; }
        using (var dlg = new SaveFileDialog { Filter = "Wavefront OBJ (*.obj)|*.obj", FileName = Path.GetFileNameWithoutExtension(importedPath) + ".obj" })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            string baseName = Path.Combine(Path.GetDirectoryName(dlg.FileName), Path.GetFileNameWithoutExtension(dlg.FileName));
            var ci = CultureInfo.InvariantCulture;
            // One livery PNG per model that has one; map_Kd points to it.
            var liveryFiles = new Dictionary<ModelInfo, string>();
            foreach (var m in meshes.Select(x => x.Model).Distinct().Where(x => x.Livery != null))
            {
                string png = baseName + (liveryFiles.Count == 0 ? "" : "_" + liveryFiles.Count) + "_livery0hi.png";
                SavePng(texPixels[m.Livery], texSize[m.Livery], png);
                liveryFiles[m] = Path.GetFileName(png);
            }
            var mtl = new StringBuilder("# FM1 materials (Turn10 names, default.xbe enum) - FM1 Studio\n");
            var written = new HashSet<string>();
            foreach (var m in meshes)
            {
                string name = MtlName(m, liveryFiles.Count > 1);
                if (!written.Add(name)) continue;
                var c = Utilities(m.Sub.Material);
                mtl.AppendFormat(ci, "newmtl {0}\nKd {1:0.###} {2:0.###} {3:0.###}\n", name, c.X, c.Y, c.Z);
                string png;
                if (liveryFiles.TryGetValue(m.Model, out png)) mtl.AppendLine("map_Kd " + png);
                mtl.AppendLine();
            }
            File.WriteAllText(baseName + ".mtl", mtl.ToString());
            using (var w = new StreamWriter(dlg.FileName))
            {
                w.WriteLine("# Extracted by FM1 Studio from " + Path.GetFileName(importedPath) + ". Units: metres, front = -Z.");
                w.WriteLine("mtllib " + Path.GetFileName(baseName) + ".mtl");
                int baseIndex = 1;
                // Group by part (one vertex block per part), keeping the tree order.
                foreach (var g in meshes.GroupBy(x => new { x.Model, x.Part, x.Sub.Lod }))
                {
                    var p = g.Key.Part;
                    w.WriteLine("g " + (p.Name ?? "Part"));
                    for (int v = 0; v < p.Count; v++) w.WriteLine(string.Format(ci, "v {0} {1} {2}", p.Pos[v * 3], p.Pos[v * 3 + 1], -p.Pos[v * 3 + 2]));
                    for (int v = 0; v < p.Count; v++) w.WriteLine(string.Format(ci, "vt {0} {1}", p.Uv[v * 2], 1 - p.Uv[v * 2 + 1]));
                    for (int v = 0; v < p.Count; v++) w.WriteLine(string.Format(ci, "vn {0} {1} {2}", p.Nrm[v * 3], p.Nrm[v * 3 + 1], -p.Nrm[v * 3 + 2]));
                    foreach (var m in g)
                    {
                        w.WriteLine("usemtl " + MtlName(m, liveryFiles.Count > 1));
                        foreach (var t in Fm1Car.Triangles(m.Sub.Strip, false))
                            w.WriteLine("f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}", baseIndex + t[0], baseIndex + t[1], baseIndex + t[2]);
                    }
                    baseIndex += p.Count;
                }
            }
            status.Text = "extracted " + meshes.Count + " meshes to " + dlg.FileName;
            MessageBox.Show(this, "Extraction Complete!", "FM1 Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // With several models in one OBJ (e.g. Mega.ca2 wheels) the materials of different textures must not merge.
    static string MtlName(MeshTag m, bool perModel)
    {
        return perModel ? Path.GetFileName(Path.GetDirectoryName(m.Model.Path) ?? "") + "_" + m.Material : m.Material;
    }

    static Vector3 Utilities(int m)
    {
        if (m == 1) return new Vector3(0.6f);
        if (m == 2 || m == 28) return new Vector3(0.55f, 0.65f, 0.75f);
        if (m == 7 || m == 17 || m == 22 || m == 23) return new Vector3(0.85f);
        if (m == 8 || m == 9 || m == 10 || m == 21) return new Vector3(0.12f);
        if (m == 3 || m == 16 || m == 30) return new Vector3(0.8f, 0.1f, 0.1f);
        return new Vector3(0.55f);
    }

    // ---------------- textures tab ----------------

    void ShowTexture(TextureTag t)
    {
        if (t == null) return;
        try
        {
            var x = XprTexture.Parse(t.Read());
            var bmp = ToBitmap(x);
            if (picture.Image != null) picture.Image.Dispose();
            picture.Image = bmp;
            status.Text = t.Path + "   " + x.Describe() + (x.Depth > 1 ? "   (volume: slices left to right)" : "");
        }
        catch (Exception ex) { status.Text = t.Path + ": " + ex.Message; }
    }

    static Bitmap ToBitmap(XprTexture x)
    {
        int w, h;
        int[] px = x.DecodeTop(out w, out h);
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var bd = bmp.LockBits(new DRectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < h; y++) Marshal.Copy(px, y * w, bd.Scan0 + y * bd.Stride, w);
        bmp.UnlockBits(bd);
        return bmp;
    }

    static void SavePng(int[] px, Size size, string file)
    {
        using (var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb))
        {
            var bd = bmp.LockBits(new DRectangle(0, 0, size.Width, size.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < size.Height; y++) Marshal.Copy(px, y * size.Width, bd.Scan0 + y * bd.Stride, size.Width);
            bmp.UnlockBits(bd);
            bmp.Save(file, ImageFormat.Png);
        }
    }

    void SaveTextureAs()
    {
        var t = tvTextures.SelectedNode != null ? tvTextures.SelectedNode.Tag as TextureTag : null;
        if (t == null) return;
        using (var dlg = new SaveFileDialog { Filter = "PNG (*.png)|*.png", FileName = Path.GetFileNameWithoutExtension(t.Path) + ".png" })
            if (dlg.ShowDialog(this) == DialogResult.OK)
                using (var bmp = ToBitmap(XprTexture.Parse(t.Read()))) bmp.Save(dlg.FileName, ImageFormat.Png);
    }

    void ExtractTextures(bool checkedOnly)
    {
        var list = tvTextures.Nodes.Cast<TreeNode>().Where(n => !checkedOnly || n.Checked).Select(n => (TextureTag)n.Tag).ToList();
        if (list.Count == 0) { MessageBox.Show(this, "No textures to extract.", "FM1 Studio"); return; }
        using (var dlg = new FolderBrowserDialog { Description = "Folder for the PNG files (the archive paths are kept)" })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            int done = 0;
            foreach (var t in list)
            {
                try
                {
                    string dest = Path.Combine(dlg.SelectedPath, Path.ChangeExtension(t.Path, ".png"));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    using (var bmp = ToBitmap(XprTexture.Parse(t.Read()))) bmp.Save(dest, ImageFormat.Png);
                    done++;
                }
                catch (Exception ex) { status.Text = t.Path + ": " + ex.Message; }
            }
            MessageBox.Show(this, done + " texture(s) extracted.", "FM1 Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // ---------------- view ----------------

    static Vector3 V(float[] a, int v) { return new Vector3(a[v * 3], a[v * 3 + 1], -a[v * 3 + 2]); }

    static VertexPositionNormalTexture[] Build(MeshTag m)
    {
        if (m.Verts != null) return m.Verts;
        var p = m.Part;
        var tris = Fm1Car.Triangles(m.Sub.Strip, false).ToList();
        var verts = new VertexPositionNormalTexture[tris.Count * 3];
        int i = 0;
        foreach (var t in tris)
            foreach (int v in t)
                verts[i++] = new VertexPositionNormalTexture(V(p.Pos, v), V(p.Nrm, v), new Vector2(p.Uv[v * 2], p.Uv[v * 2 + 1]));
        m.Verts = verts;
        return verts;
    }

    string TextureKey(MeshTag m)
    {
        if (m.Model.Livery == null) return null;
        return m.Sub.Material == 1 ? m.Model.Livery + "+paint" : m.Sub.Material == 19 ? m.Model.Livery + "+carbon" : m.Model.Livery;
    }

    void MeshBounds(IEnumerable<MeshTag> ms, out Vector3 mn, out Vector3 mx)
    {
        mn = new Vector3(1e9f);
        mx = new Vector3(-1e9f);
        foreach (var m in ms) foreach (var v in Build(m)) { mn = Vector3.Min(mn, v.Position); mx = Vector3.Max(mx, v.Position); }
    }

    void FrameChecked() { FrameMeshes(Meshes(tvModels.Nodes, true).ToList()); }

    void FrameNode(TreeNode n)
    {
        var list = n.Tag is MeshTag ? new List<MeshTag> { (MeshTag)n.Tag } : Meshes(n.Nodes, false).ToList();
        FrameMeshes(list, 1.6f);
    }

    void FrameMeshes(List<MeshTag> list, float k = 1.1f)
    {
        if (list.Count == 0) return;
        Vector3 mn, mx;
        MeshBounds(list, out mn, out mx);
        if (mn.X > mx.X) return;
        target = (mn + mx) / 2;
        distance = Math.Max(0.2f, (mx - mn).Length() * k);
    }

    void InitGraphics()
    {
        try
        {
            var pp = new PresentationParameters
            {
                IsFullScreen = false,
                BackBufferCount = 1,
                BackBufferWidth = Math.Max(1, view.Width),
                BackBufferHeight = Math.Max(1, view.Height),
                BackBufferFormat = SurfaceFormat.Unknown,
                EnableAutoDepthStencil = true,
                AutoDepthStencilFormat = DepthFormat.Depth24Stencil8,
                PresentationInterval = PresentInterval.Default
            };
            device = new GraphicsDevice(GraphicsAdapter.DefaultAdapter, DeviceType.Hardware, view.Handle, pp);
            effect = new BasicEffect(device, null);
            effect.EnableDefaultLighting();
            effect.PreferPerPixelLighting = true;
            effect.SpecularPower = 24f;
            effect.SpecularColor = new Vector3(0.25f);
            effect.AmbientLightColor = new Vector3(0.45f);
            lineEffect = new BasicEffect(device, null);
            lineEffect.VertexColorEnabled = true;
            vdecl = new VertexDeclaration(device, VertexPositionNormalTexture.VertexElements);
            ldecl = new VertexDeclaration(device, VertexPositionColor.VertexElements);
            graphicsFailed = false;
        }
        catch (Exception ex)
        {
            graphicsFailed = true;
            status.Text = "3D view disabled: " + ex.Message;
        }
    }

    void DisposeGraphics()
    {
        foreach (var t in gpuTex.Values) t.Dispose();
        gpuTex.Clear();
        if (vdecl != null) vdecl.Dispose();
        if (ldecl != null) ldecl.Dispose();
        if (effect != null) effect.Dispose();
        if (lineEffect != null) lineEffect.Dispose();
        if (device != null) device.Dispose();
        device = null;
    }

    void ResetDevice()
    {
        if (device == null || view.Width < 1 || view.Height < 1) return;
        DisposeGraphics();
        InitGraphics();
        RedrawView();
    }

    void RedrawView() { view.Invalidate(); }

    Texture2D Gpu(string key)
    {
        if (key == null) return null;
        Texture2D t;
        if (gpuTex.TryGetValue(key, out t)) return t;
        int[] px;
        if (!texPixels.TryGetValue(key, out px)) return null;
        var size = texSize[key];
        t = new Texture2D(device, size.Width, size.Height, 0, TextureUsage.AutoGenerateMipMap, SurfaceFormat.Color);
        t.SetData(px);
        t.GenerateMipMaps(TextureFilter.Linear);
        gpuTex[key] = t;
        return t;
    }

    void Render()
    {
        if (device == null || graphicsFailed || !view.Visible) return;
        try
        {
            var eye = target + distance * new Vector3((float)(Math.Cos(pitch) * Math.Sin(yaw)), (float)Math.Sin(pitch), (float)(Math.Cos(pitch) * Math.Cos(yaw)));
            var viewM = Matrix.CreateLookAt(eye, target, Vector3.Up);
            var proj = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(45), Math.Max(1, view.Width) / (float)Math.Max(1, view.Height), Math.Max(0.005f, distance / 500), distance * 50 + 100);
            device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, new XColor(150, 150, 150), 1f, 0);
            device.RenderState.CullMode = CullMode.None;
            device.RenderState.AlphaBlendEnable = false;
            DrawShowroom();
            device.RenderState.DepthBufferEnable = true;
            device.RenderState.DepthBufferWriteEnable = true;
            device.SamplerStates[0].AddressU = TextureAddressMode.Wrap;
            device.SamplerStates[0].AddressV = TextureAddressMode.Wrap;
            device.SamplerStates[0].MinFilter = TextureFilter.Linear;
            device.SamplerStates[0].MagFilter = TextureFilter.Linear;
            device.SamplerStates[0].MipFilter = TextureFilter.Linear;
            lineEffect.View = viewM;
            lineEffect.Projection = proj;
            lineEffect.World = Matrix.Identity;
            DrawGrid();

            effect.View = viewM;
            effect.Projection = proj;
            effect.World = Matrix.Identity;
            device.VertexDeclaration = vdecl;
            var shown = Meshes(tvModels.Nodes, true).ToList();
            DrawMeshes(shown.Where(m => !Fm1Materials.Transparent(m.Sub.Material)), false);
            device.RenderState.AlphaBlendEnable = true;
            device.RenderState.SourceBlend = Blend.SourceAlpha;
            device.RenderState.DestinationBlend = Blend.InverseSourceAlpha;
            device.RenderState.DepthBufferWriteEnable = false;
            DrawMeshes(shown.Where(m => Fm1Materials.Transparent(m.Sub.Material)), false);
            device.RenderState.AlphaBlendEnable = false;
            device.RenderState.DepthBufferWriteEnable = true;
            if (mnuWireframe.Checked)
            {
                device.RenderState.FillMode = FillMode.WireFrame;
                device.RenderState.DepthBias = -0.00002f;
                DrawMeshes(shown, true);
                device.RenderState.FillMode = FillMode.Solid;
                device.RenderState.DepthBias = 0;
            }
            device.Present();
        }
        catch (DeviceLostException) { }
        catch (Exception ex) { status.Text = "render: " + ex.Message; }
    }

    // Highlight: the selected mesh, or every mesh of the selected part (not for whole LODs or files).
    bool Selected(MeshTag m)
    {
        if (selectedMesh != null) return m == selectedMesh;
        if (selectedNode == null || selectedNode.Level != 2) return false;
        foreach (TreeNode n in selectedNode.Nodes) if (n.Tag == m) return true;
        return false;
    }

    void DrawMeshes(IEnumerable<MeshTag> ms, bool wire)
    {
        foreach (var m in ms)
        {
            var verts = Build(m);
            if (verts.Length == 0) continue;
            bool sel = Selected(m);
            var tex = (!wire && mnuTextures.Checked) ? Gpu(TextureKey(m)) : null;
            effect.TextureEnabled = tex != null;
            effect.Texture = tex;
            effect.LightingEnabled = !wire;
            effect.DiffuseColor = wire ? (sel ? new Vector3(1f, 0.82f, 0.1f) : new Vector3(0.05f)) : tex != null ? Vector3.One : MaterialColour(m);
            if (sel && !wire) effect.DiffuseColor *= new Vector3(1f, 0.85f, 0.25f);    // FM1 yellow
            effect.Alpha = Fm1Materials.Transparent(m.Sub.Material) && !wire ? 0.55f : 1f;
            effect.Begin();
            foreach (EffectPass pass in effect.CurrentTechnique.Passes)
            {
                pass.Begin();
                device.DrawUserPrimitives(PrimitiveType.TriangleList, verts, 0, verts.Length / 3);
                pass.End();
            }
            effect.End();
        }
    }

    static Vector3 MaterialColour(MeshTag m)
    {
        if (m.Sub.Material == 1) return new Vector3(m.Model.Paint.R / 255f, m.Model.Paint.G / 255f, m.Model.Paint.B / 255f);
        return Utilities(m.Sub.Material);
    }

    // FM1 showroom backdrop: grey at the edges, nearly white in the middle.
    void DrawShowroom()
    {
        var edge = new XColor(150, 150, 150);
        var mid = new XColor(238, 238, 238);
        var v = new[]
        {
            new VertexPositionColor(new Vector3(-1, 1, 0.5f), edge), new VertexPositionColor(new Vector3(1, 1, 0.5f), edge), new VertexPositionColor(new Vector3(-1, 0.1f, 0.5f), mid),
            new VertexPositionColor(new Vector3(1, 1, 0.5f), edge), new VertexPositionColor(new Vector3(1, 0.1f, 0.5f), mid), new VertexPositionColor(new Vector3(-1, 0.1f, 0.5f), mid),
            new VertexPositionColor(new Vector3(-1, 0.1f, 0.5f), mid), new VertexPositionColor(new Vector3(1, 0.1f, 0.5f), mid), new VertexPositionColor(new Vector3(-1, -1, 0.5f), edge),
            new VertexPositionColor(new Vector3(1, 0.1f, 0.5f), mid), new VertexPositionColor(new Vector3(1, -1, 0.5f), edge), new VertexPositionColor(new Vector3(-1, -1, 0.5f), edge),
        };
        device.RenderState.DepthBufferEnable = false;
        lineEffect.World = Matrix.Identity;
        lineEffect.View = Matrix.Identity;
        lineEffect.Projection = Matrix.Identity;
        device.VertexDeclaration = ldecl;
        lineEffect.Begin();
        foreach (EffectPass pass in lineEffect.CurrentTechnique.Passes)
        {
            pass.Begin();
            device.DrawUserPrimitives(PrimitiveType.TriangleList, v, 0, 4);
            pass.End();
        }
        lineEffect.End();
    }

    void DrawGrid()
    {
        var l = new List<VertexPositionColor>();
        var g = new XColor(175, 175, 175);
        for (int i = -5; i <= 5; i++)
        {
            var c = i == 0 ? new XColor(240, 138, 0) : g;
            var c2 = i == 0 ? new XColor(40, 110, 200) : g;
            l.Add(new VertexPositionColor(new Vector3(-5, 0, i), c)); l.Add(new VertexPositionColor(new Vector3(5, 0, i), c));
            l.Add(new VertexPositionColor(new Vector3(i, 0, -5), c2)); l.Add(new VertexPositionColor(new Vector3(i, 0, 5), c2));
        }
        device.VertexDeclaration = ldecl;
        lineEffect.Begin();
        foreach (EffectPass pass in lineEffect.CurrentTechnique.Passes)
        {
            pass.Begin();
            device.DrawUserPrimitives(PrimitiveType.LineList, l.ToArray(), 0, l.Count / 2);
            pass.End();
        }
        lineEffect.End();
    }

    void OnViewMouseMove(object sender, MouseEventArgs e)
    {
        if (dragButton == MouseButtons.None) return;
        float dx = e.X - lastMouse.X, dy = e.Y - lastMouse.Y;
        lastMouse = e.Location;
        if (dragButton == MouseButtons.Left)
        {
            yaw -= dx * 0.01f;
            pitch = MathHelper.Clamp(pitch + dy * 0.01f, -1.5f, 1.5f);
        }
        else
        {
            if (Math.Abs(dx) + Math.Abs(dy) > 0) panned = true;
            var forward = new Vector3((float)(Math.Cos(pitch) * Math.Sin(yaw)), (float)Math.Sin(pitch), (float)(Math.Cos(pitch) * Math.Cos(yaw)));
            var rightV = Vector3.Normalize(Vector3.Cross(Vector3.Up, forward));
            var upV = Vector3.Cross(forward, rightV);
            float k = distance * 0.0015f;
            target += -rightV * dx * k + upV * dy * k;
        }
        RedrawView();
    }
}

static class Fm1Studio
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.Run(new StudioForm(args.Length > 0 ? args[0] : null));
    }
}

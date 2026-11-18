using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml.Serialization;

public class Meaning {
  public string Pos;
  public string Text;

  public override string ToString() {
    return Pos + " " + Text;
  }
}

public class Word {
  public string Id = Guid.NewGuid().ToString();
  public string Text;
  public int Page;
  public List<Meaning> Meanings = new List<Meaning>();
}

public class Library {
  public string Name;
  public List<Word> Words = new List<Word>();

  public override string ToString() {
    return Name;
  }
}

public class Answer {
  public Word Word;
  public Meaning Prompt;
  public List<string> Attempts = new List<string>();
  public List<string> Hints = new List<string>();
  public bool Correct;
  public bool Skipped;
  public bool Weak {
    get { return !Correct || Attempts.Count > 1 || Hints.Count > 0; }
  }
}

public class Round {
  public string Library;
  public DateTime Time;
  public string Range;
  public List<Answer> Answers = new List<Answer>();

  public override string ToString() {
    return Time.ToString("MM-dd HH:mm:ss") + "  " + Library + "  " + Range + "  答对 " +
           Answers.Count(a => a.Correct) + "/" + Answers.Count;
  }
}

public class Data {
  public List<Library> Libraries = new List<Library>();
  public List<Round> History = new List<Round>();
}
public static class Storage {
  public static string Folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
  public static string FileName = Path.Combine(Folder, "vocabulary.xml");

  public static Data Load(string path) {
    using (var s = File.OpenRead(path)) {
      return (Data) new XmlSerializer(typeof(Data)).Deserialize(s);
    }
  }

  public static void Save(Data d) {
    Directory.CreateDirectory(Folder);
    string temp = FileName + ".tmp";
    using (var s = File.Create(temp)) {
      new XmlSerializer(typeof(Data)).Serialize(s, d);
    }
    if (File.Exists(FileName))
      File.Replace(temp, FileName, FileName + ".bak");
    else
      File.Move(temp, FileName);
  }
}
public static class Rules {

  public static string Normalize(string s) {
    return Regex.Replace(s.Trim(), " +", " ").ToLowerInvariant();
  }

  public static bool Matches(string a, string b) {
    return Normalize(a) == Normalize(b);
  }
}

static class Program {
  [STAThread]

  static void Main(string[] args) {
    if (args.Contains("--test")) {
      Tests.Run();
      return;
    }
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    try {
      Application.Run(new MainForm());
    } catch (Exception e) {
      MessageBox.Show("程序无法继续，请保留 data 文件夹。\n" + e.Message, "错误");
    }
  }
}

public class MainForm : Form {
  Data data;
  ComboBox libraries = new ComboBox();
  DataGridView grid = new DataGridView();
  TextBox search = new TextBox();
  NumericUpDown start = Num(1), end = Num(999), count = Num(20);
  Library Current {
    get { return libraries.SelectedItem as Library; }
  }

  public static NumericUpDown Num(int value) {
    return new NumericUpDown { Minimum = 1, Maximum = 1000000, Value = value, Width = 90 };
  }

  public static Button Btn(string text, Action action) {
    var b = new Button { Text = text, AutoSize = true, Height = 34, Margin = new Padding(5) };
    b.Click += (s, e) => {
      try {
        action();
      } catch (Exception ex) {
        MessageBox.Show(ex.Message, "操作未完成");
      }
    };
    return b;
  }

  public static FlowLayoutPanel Bar() {
    return new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8),
                                 WrapContents = true };
  }

  public MainForm() {
    Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
    Text = "单词抽背";
    Width = 1100;
    Height = 740;
    MinimumSize = new Size(850, 600);
    Font = new Font("Microsoft YaHei UI", 10);
    StartPosition = FormStartPosition.CenterScreen;
    data = File.Exists(Storage.FileName) ? Storage.Load(Storage.FileName) : new Data();
    Storage.Save(data);
    var top = Bar();
    libraries.Width = 200;
    libraries.DropDownStyle = ComboBoxStyle.DropDownList;
    libraries.SelectedIndexChanged += (s, e) => RefreshWords();
    top.Controls.Add(libraries);
    top.Controls.Add(Btn("新建词库", () => {
      string name = Ask("词库名称", "");
      if (name == null)
        return;
      if (name.Trim() == "" || data.Libraries.Any(l => l.Name == name.Trim()))
        throw new Exception("名称不能为空或重复");
      data.Libraries.Add(new Library { Name = name.Trim() });
      Save();
      RefreshLibraries(data.Libraries.Last());
    }));
    top.Controls.Add(Btn("重命名", () => {
      if (Current == null)
        return;
      string n = Ask("词库名称", Current.Name);
      if (n == null)
        return;
      if (n.Trim() == "" || data.Libraries.Any(l => l != Current && l.Name == n.Trim()))
        throw new Exception("名称不能为空或重复");
      var c = Current;
      c.Name = n.Trim();
      Save();
      RefreshLibraries(c);
    }));
    top.Controls.Add(Btn("删除词库", () => {
      if (Current != null && Confirm("删除整个词库及全部单词？")) {
        data.Libraries.Remove(Current);
        Save();
        RefreshLibraries(null);
      }
    }));
    top.Controls.Add(Btn("最近五轮", History));
    top.Controls.Add(Btn("备份", Backup));
    top.Controls.Add(Btn("恢复", Restore));
    var tools = Bar();
    tools.Controls.Add(Btn("添加单词", () => Edit(null)));
    tools.Controls.Add(Btn("编辑所选", () => {
      var w = Selected();
      if (w != null)
        Edit(w);
    }));
    tools.Controls.Add(Btn("删除所选", () => {
      if (Current == null)
        return;
      var ws = grid.SelectedRows.Cast<DataGridViewRow>().Select(r => (Word)r.Tag).ToList();
      if (ws.Count > 0 && Confirm("删除选中的 " + ws.Count + " 个单词？")) {
        foreach (var w in ws)
          Current.Words.Remove(w);
        Save();
        RefreshWords();
      }
    }));
    tools.Controls.Add(Btn("批量导入", Import));
    tools.Controls.Add(
      new Label { Text = "搜索单词 / 意思 / 页码", AutoSize = true, Margin = new Padding(10) });
    search.Width = 200;
    search.TextChanged += (s, e) => RefreshWords();
    tools.Controls.Add(search);
    var setup = Bar();
    setup.Controls.Add(new Label { Text = "页码从", AutoSize = true, Margin = new Padding(8) });
    setup.Controls.Add(start);
    setup.Controls.Add(new Label { Text = "至", AutoSize = true, Margin = new Padding(8) });
    setup.Controls.Add(end);
    setup.Controls.Add(new Label { Text = "抽背数量", AutoSize = true, Margin = new Padding(8) });
    setup.Controls.Add(count);
    setup.Controls.Add(Btn("开始抽背", Start));
    grid.Dock = DockStyle.Fill;
    grid.ReadOnly = true;
    grid.AllowUserToAddRows = false;
    grid.AllowUserToDeleteRows = false;
    grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
    grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
    grid.RowHeadersVisible = false;
    grid.Columns.Add("word", "单词");
    grid.Columns.Add("page", "页码");
    grid.Columns.Add("meaning", "词性与意思");
    grid.Columns[2].FillWeight = 250;
    grid.DoubleClick += (s, e) => {
      var w = Selected();
      if (w != null)
        Edit(w);
    };
    Controls.Add(grid);
    Controls.Add(setup);
    Controls.Add(tools);
    Controls.Add(top);
    RefreshLibraries(null);
  }

  void Save() {
    Storage.Save(data);
  }

  public static bool Confirm(string s) {
    return MessageBox.Show(s, "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
  }

  public static string Ask(string title, string value) {
    using (var f = new Form { Text = title, Width = 440, Height = 160,
                              StartPosition = FormStartPosition.CenterParent }) {
      var t = new TextBox { Text = value, Dock = DockStyle.Top };
      var b = Btn("确定", () => f.DialogResult = DialogResult.OK);
      b.Dock = DockStyle.Bottom;
      f.Controls.Add(t);
      f.Controls.Add(b);
      f.AcceptButton = b;
      return f.ShowDialog() == DialogResult.OK ? t.Text : null;
    }
  }

  void RefreshLibraries(Library select) {
    libraries.Items.Clear();
    foreach (var l in data.Libraries)
      libraries.Items.Add(l);
    if (select != null)
      libraries.SelectedItem = select;
    else if (libraries.Items.Count > 0)
      libraries.SelectedIndex = 0;
    RefreshWords();
  }

  void RefreshWords() {
    grid.Rows.Clear();
    if (Current == null)
      return;
    foreach (var w in Current.Words.OrderBy(w => w.Page).ThenBy(w => w.Text)) {
      string m = string.Join("；", w.Meanings.Select(x => x.ToString()));
      if ((w.Text + " " + w.Page + " " + m).IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) < 0)
        continue;
      int i = grid.Rows.Add(w.Text, w.Page, m);
      grid.Rows[i].Tag = w;
    }
    Text = "单词抽背 — " + Current.Name + "（" + Current.Words.Count + " 词）";
  }

  Word Selected() {
    return grid.SelectedRows.Count > 0 ? grid.SelectedRows[0].Tag as Word : null;
  }

  void Edit(Word w) {
    if (Current == null)
      throw new Exception("请先创建或选择词库");
    using (var f = new WordEditor(w)) {
      if (f.ShowDialog(this) != DialogResult.OK)
        return;
      var result = f.Result;
      if (Current.Words.Any(x => x != w && Rules.Matches(x.Text, result.Text)))
        throw new Exception("词库已存在该单词");
      if (w != null)
        Current.Words.Remove(w);
      Current.Words.Add(result);
      Save();
      RefreshWords();
    }
  }

  void Import() {
    if (Current == null)
      throw new Exception("请先选择词库");
    using (var f = new ImportForm()) {
      if (f.ShowDialog(this) != DialogResult.OK)
        return;
      int added = 0;
      foreach (var w in f.Words) {
        if (Current.Words.Any(x => Rules.Matches(x.Text, w.Text)))
          continue;
        Current.Words.Add(w);
        added++;
      }
      Save();
      RefreshWords();
      MessageBox.Show("导入 " + added + " 个，重复跳过 " + (f.Words.Count - added) + " 个。");
    }
  }

  void Start() {
    if (Current == null)
      return;
    if (start.Value > end.Value)
      throw new Exception("起始页码不能大于结束页码");
    var words = Current.Words.Where(w => w.Page >= start.Value && w.Page <= end.Value).ToList();
    if (words.Count == 0)
      throw new Exception("该范围没有单词");
    if (count.Value > words.Count)
      MessageBox.Show("范围内只有 " + words.Count + " 个单词，将全部抽背。");
    var rng = new Random();
    RunRound(words.OrderBy(w => rng.Next()).Take((int)count.Value).ToList(), Current.Name,
             start.Value + "–" + end.Value, null);
  }

  void RunRound(List<Word> words, string name, string range, List<Answer> prior) {
    while (words.Count > 0) {
      using (var f = new QuizForm(words, name, range, prior)) {
        if (f.ShowDialog(this) != DialogResult.OK)
          return;
        var round = f.Result;
        data.History.Insert(0, round);
        data.History = data.History.Take(5).ToList();
        Save();
        using (var results = new ResultForm(round, true)) {
          results.ShowDialog(this);
          if (!results.Repeat)
            return;
        }
        prior = round.Answers.Where(a => a.Weak).ToList();
        var rng = new Random();
        words = prior.Select(a => a.Word).OrderBy(w => rng.Next()).ToList();
        range = "加深记忆";
      }
    }
  }

  void History() {
    using (var f = new Form { Text = "最近五次抽背（包含加深记忆）", Width = 800, Height = 400,
                              StartPosition = FormStartPosition.CenterParent }) {
      var list = new ListBox { Dock = DockStyle.Fill };
      foreach (var r in data.History)
        list.Items.Add(r);
      list.DoubleClick += (s, e) => {
        if (list.SelectedItem != null)
          using (var result = new ResultForm((Round)list.SelectedItem, false)) result.ShowDialog(f);
      };
      f.Controls.Add(list);
      f.ShowDialog(this);
    }
  }

  void Backup() {
    using (var d = new SaveFileDialog { Filter = "词库备份|*.xml", FileName = "单词抽背备份.xml" }) {
      if (d.ShowDialog() != DialogResult.OK)
        return;
      Save();
      File.Copy(Storage.FileName, d.FileName, true);
    }
  }

  void Restore() {
    using (var d = new OpenFileDialog { Filter = "词库备份|*.xml" }) {
      if (d.ShowDialog() != DialogResult.OK)
        return;
      var restored = Storage.Load(d.FileName);
      if (restored == null || restored.Libraries == null || restored.History == null ||
          restored.Libraries.Any(l => l.Words == null ||
                                      l.Words.Any(w => w.Page < 1 || string.IsNullOrWhiteSpace(w.Text) ||
                                                       w.Meanings == null || w.Meanings.Count == 0)))
        throw new Exception("备份内容不完整");
      if (!Confirm("用此备份覆盖全部当前词库和历史？当前文件会保留为 .bak。"))
        return;
      restored.History = restored.History.Take(5).ToList();
      Storage.Save(restored);
      data = restored;
      RefreshLibraries(null);
    }
  }
}

public class WordEditor : Form {
  TextBox word = new TextBox { Width = 240 };
  NumericUpDown page = MainForm.Num(1);
  FlowLayoutPanel groups =
    new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
                          WrapContents = false };
  Dictionary<string, FlowLayoutPanel> sections = new Dictionary<string, FlowLayoutPanel>();
  Dictionary<string, CheckBox> checks = new Dictionary<string, CheckBox>();
  Word original;
  public Word Result;

  public WordEditor(Word w) {
    original = w;
    Text = w == null ? "添加单词" : "编辑单词";
    Width = 760;
    Height = 650;
    StartPosition = FormStartPosition.CenterParent;
    Font = new Font("Microsoft YaHei UI", 10);
    var top = MainForm.Bar();
    top.Controls.Add(new Label { Text = "单词", AutoSize = true });
    top.Controls.Add(word);
    top.Controls.Add(new Label { Text = "页码", AutoSize = true });
    top.Controls.Add(page);
    var types = MainForm.Bar();
    types.MaximumSize = new Size(730, 0);
    var names = new List<string> { "n.",   "v.",   "adj.",    "adv.", "prep.", "pron.", "conj.",
                                   "num.", "art.", "interj.", "aux.", "phr.",  "vt.",   "vi." };
    if (w != null)
      names.AddRange(w.Meanings.Select(m => m.Pos).Except(names));
    foreach (string p in names)
      AddType(types, p, w);
    types.Controls.Add(MainForm.Btn("自定义词性", () => {
      string p = MainForm.Ask("词性缩写", "");
      if (!string.IsNullOrWhiteSpace(p) && !checks.ContainsKey(p.Trim()))
        AddType(types, p.Trim(), null);
    }));
    var bottom = MainForm.Bar();
    bottom.Dock = DockStyle.Bottom;
    bottom.Controls.Add(MainForm.Btn("保存", SaveWord));
    bottom.Controls.Add(MainForm.Btn("取消", () => Close()));
    Controls.Add(groups);
    Controls.Add(bottom);
    Controls.Add(types);
    Controls.Add(top);
    if (w != null) {
      word.Text = w.Text;
      page.Value = w.Page;
    }
  }

  void AddType(FlowLayoutPanel types, string pos, Word w) {
    var section = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown,
                                        WrapContents = false, Visible = false, Width = 680 };
    section.Controls.Add(new Label { Text = pos, AutoSize = true });
    var add = MainForm.Btn("＋ 添加一个意思", () => AddMeaning(section, ""));
    section.Controls.Add(add);
    groups.Controls.Add(section);
    var check = new CheckBox { Text = pos, AutoSize = true };
    types.Controls.Add(check);
    checks[pos] = check;
    sections[pos] = section;
    check.CheckedChanged += (s, e) => {
      section.Visible = check.Checked;
      if (check.Checked && section.Controls.OfType<FlowLayoutPanel>().Count() == 0)
        AddMeaning(section, "");
    };
    if (w != null && w.Meanings.Any(m => m.Pos == pos)) {
      check.Checked = true;
      foreach (var row in section.Controls.OfType<FlowLayoutPanel>().ToList()) {
        section.Controls.Remove(row);
        row.Dispose();
      }
      foreach (var m in w.Meanings.Where(m => m.Pos == pos))
        AddMeaning(section, m.Text);
    }
  }

  void AddMeaning(FlowLayoutPanel section, string value) {
    var row = new FlowLayoutPanel { AutoSize = true, Width = 660 };
    row.Controls.Add(new TextBox { Text = value, Width = 500 });
    row.Controls.Add(MainForm.Btn("删除", () => {
      section.Controls.Remove(row);
      row.Dispose();
    }));
    section.Controls.Add(row);
    section.Controls.SetChildIndex(row, section.Controls.Count - 2);
  }

  void SaveWord() {
    if (string.IsNullOrWhiteSpace(word.Text))
      throw new Exception("请输入单词");
    var result = new Word { Text = Regex.Replace(word.Text.Trim(), " +", " "), Page = (int)page.Value };
    if (original != null)
      result.Id = original.Id;
    foreach (var item in checks.Where(x => x.Value.Checked)) {
      var values = sections[item.Key]
                     .Controls.OfType<FlowLayoutPanel>()
                     .Select(r => r.Controls.OfType<TextBox>().First().Text.Trim())
                     .ToList();
      if (values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
        throw new Exception(item.Key + " 请至少填写一个意思，不能留空");
      if (values.Distinct().Count() != values.Count)
        throw new Exception(item.Key + " 存在重复意思");
      result.Meanings.AddRange(values.Select(v => new Meaning { Pos = item.Key, Text = v }));
    }
    if (result.Meanings.Count == 0)
      throw new Exception("请勾选至少一个词性并填写意思");
    Result = result;
    DialogResult = DialogResult.OK;
  }
}

public class ImportForm : Form {
  TextBox input = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both,
                                AcceptsReturn = true };
  TextBox preview = new TextBox { Dock = DockStyle.Bottom, Height = 180, Multiline = true, ReadOnly = true,
                                  ScrollBars = ScrollBars.Vertical };
  Button commit;
  public List<Word> Words = new List<Word>();

  public static List<Word> Parse(string text) {
    var words = new List<Word>();
    int line = 0;
    foreach (var raw in text.Split('\n')) {
      line++;
      if (string.IsNullOrWhiteSpace(raw))
        continue;
      try {
        var parts = raw.Trim().Split('|').Select(x => x.Trim()).ToArray();
        int page;
        if (parts.Length < 3 || string.IsNullOrWhiteSpace(parts[0]) || !int.TryParse(parts[1], out page) ||
            page < 1 || page > 1000000)
          throw new Exception("需要单词、整数页码和词性意思");
        var w = new Word { Text = parts[0], Page = page };
        foreach (var part in parts.Skip(2)) {
          int colon = part.IndexOfAny(new[] { ':', '：' });
          if (colon < 1)
            throw new Exception("词性后需要冒号");
          string pos = part.Substring(0, colon).Trim();
          var values = part.Substring(colon + 1).Split(new[] { ';', '；' }).Select(x => x.Trim()).ToList();
          if (values.Any(string.IsNullOrWhiteSpace))
            throw new Exception("意思不能为空");
          foreach (string v in values)
            w.Meanings.Add(new Meaning { Pos = pos, Text = v });
        }
        if (w.Meanings.Select(m => m.Pos + "\n" + m.Text).Distinct().Count() != w.Meanings.Count)
          throw new Exception("有重复意思");
        words.Add(w);
      } catch (Exception ex) {
        throw new Exception("第 " + line + " 行：" + ex.Message);
      }
    }
    if (words.Count == 0)
      throw new Exception("请填写导入内容");
    return words;
  }

  public ImportForm() {
    Text = "批量导入 — 每行：单词 | 页码 | 词性: 意思；意思";
    Width = 880;
    Height = 620;
    StartPosition = FormStartPosition.CenterParent;
    Font = new Font("Microsoft YaHei UI", 10);
    input.Text = "pulse | 25 | n.: 脉搏 | v.: 搏动，跳动；洋溢着";
    var bar = MainForm.Bar();
    bar.Dock = DockStyle.Bottom;
    bar.Controls.Add(MainForm.Btn("预览并检查", () => {
      Words = Parse(input.Text);
      preview.Text = string.Join(Environment.NewLine,
                                 Words.Select(w => w.Text + " [第" + w.Page + "页] " +
                                                   string.Join("；", w.Meanings.Select(m => m.ToString()))));
      commit.Enabled = true;
    }));
    commit = MainForm.Btn("确认导入", () => DialogResult = DialogResult.OK);
    commit.Enabled = false;
    bar.Controls.Add(commit);
    input.TextChanged += (s, e) => commit.Enabled = false;
    Controls.Add(input);
    Controls.Add(preview);
    Controls.Add(bar);
  }
}

public class QuizForm : Form {
  public Round Result;
  int index;
  Answer current;
  Label progress = new Label { AutoSize = true },
        prompt = new Label { AutoSize = true, Font = new Font("Microsoft YaHei UI", 22) },
        status = new Label { AutoSize = true, MaximumSize = new Size(760, 0) };
  TextBox input = new TextBox { Width = 500, Font = new Font("Microsoft YaHei UI", 18) };
  FlowLayoutPanel hints = MainForm.Bar();
  Button submit;
  bool done, finished;

  public QuizForm(List<Word> words, string name, string range, List<Answer> prior) {
    Text = "抽背 — " + name;
    Width = 860;
    Height = 610;
    StartPosition = FormStartPosition.CenterParent;
    Font = new Font("Microsoft YaHei UI", 11);
    Result = new Round { Library = name, Range = range, Time = DateTime.Now };
    var rng = new Random();
    foreach (var w in words) {
      var previous = prior == null ? null : prior.FirstOrDefault(a => a.Word.Id == w.Id);
      Result.Answers.Add(new Answer { Word = w, Prompt = previous == null
                                                           ? w.Meanings[rng.Next(w.Meanings.Count)]
                                                           : previous.Prompt });
    }
    var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
                                       WrapContents = false, Padding = new Padding(28), AutoScroll = true };
    layout.Controls.Add(progress);
    layout.Controls.Add(prompt);
    layout.Controls.Add(input);
    submit = MainForm.Btn("提交答案（回车）", Submit);
    layout.Controls.Add(submit);
    hints.Dock = DockStyle.None;
    hints.Width = 760;
    hints.Controls.Add(MainForm.Btn(
      "查看所有意思",
      () => Hint("所有意思", string.Join("；", current.Word.Meanings.Select(m => m.ToString())))));
    hints.Controls.Add(MainForm.Btn("查看首字母", () => Hint("首字母", current.Word.Text.Substring(0, 1))));
    hints.Controls.Add(MainForm.Btn(
      "查看字母数量",
      () => Hint("字母数量", current.Word.Text.Count(char.IsLetter) + " 个字母（不含空格、连字符）")));
    layout.Controls.Add(hints);
    layout.Controls.Add(MainForm.Btn("跳过", () => {
      if (!done) {
        current.Skipped = true;
        Reveal(false);
      }
    }));
    layout.Controls.Add(status);
    Controls.Add(layout);
    AcceptButton = submit;
    FormClosing += (s, e) => {
      if (!finished && !MainForm.Confirm("结束此次未完成的抽背？本轮不保存。"))
        e.Cancel = true;
    };
    ShowQuestion();
  }

  void ShowQuestion() {
    current = Result.Answers[index];
    done = false;
    input.Clear();
    input.Enabled = true;
    hints.Enabled = true;
    progress.Text = "第 " + (index + 1) + " / " + Result.Answers.Count + " 题 · 最多尝试三次";
    prompt.Text = current.Prompt.ToString();
    status.Text = "";
    submit.Text = "提交答案（回车）";
    input.Focus();
  }

  void Hint(string type, string value) {
    if (done)
      return;
    if (!current.Hints.Contains(type))
      current.Hints.Add(type);
    status.Text = string.Join(
      "\n", current.Hints.Select(
              h => h == "所有意思" ? string.Join("；", current.Word.Meanings.Select(m => m.ToString()))
                   : h == "首字母" ? "首字母：" + current.Word.Text.Substring(0, 1)
                                   : "字母数量：" + current.Word.Text.Count(char.IsLetter)));
  }

  void Submit() {
    if (done) {
      index++;
      if (index == Result.Answers.Count) {
        finished = true;
        Result.Time = DateTime.Now;
        DialogResult = DialogResult.OK;
      } else
        ShowQuestion();
      return;
    }
    if (string.IsNullOrWhiteSpace(input.Text)) {
      status.Text = "请输入答案，或点击跳过。";
      return;
    }
    current.Attempts.Add(input.Text);
    if (Rules.Matches(input.Text, current.Word.Text))
      Reveal(true);
    else if (current.Attempts.Count >= 3)
      Reveal(false);
    else {
      status.Text = "答案不正确，还可尝试 " + (3 - current.Attempts.Count) + " 次。";
      input.SelectAll();
      input.Focus();
    }
  }

  void Reveal(bool correct) {
    current.Correct = correct;
    done = true;
    input.Enabled = false;
    hints.Enabled = false;
    status.Text = (correct ? "回答正确" : "本题未答对") + "\n正确答案：" + current.Word.Text + "\n" +
                  string.Join("\n", current.Word.Meanings.Select(m => m.ToString())) + "\n第 " +
                  current.Word.Page + " 页";
    submit.Text = index == Result.Answers.Count - 1 ? "查看统计（回车）" : "下一题（回车）";
    submit.Focus();
  }
}

public class ResultForm : Form {
  public bool Repeat;

  public ResultForm(Round r, bool allowRepeat) {
    Text = "抽背统计";
    Width = 950;
    Height = 650;
    StartPosition = FormStartPosition.CenterParent;
    Font = new Font("Microsoft YaHei UI", 10);
    var a = r.Answers;
    int weak = a.Count(x => x.Weak);
    var summary =
      new Label { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(15),
                  Text = r.ToString() + "\n首次答对：" + a.Count(x => x.Correct && x.Attempts.Count == 1) +
                         "　最终答对：" + a.Count(x => x.Correct) + "　最终未答对：" +
                         a.Count(x => !x.Correct) + "\n出现错误的词：" +
                         a.Count(x => x.Skipped || !x.Correct || x.Attempts.Count > 1) +
                         "　错误提交总次数：" + a.Sum(x => x.Attempts.Count - (x.Correct ? 1 : 0)) +
                         "　跳过：" + a.Count(x => x.Skipped) + "　不熟练：" + weak + "\n提示使用次数：" +
                         string.Join("　", new[] { "所有意思", "首字母", "字母数量" }.Select(
                                             h => h + " " + a.Count(x => x.Hints.Contains(h)))) };
    var list = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                             ScrollBars = ScrollBars.Vertical };
    list.Text = weak == 0
                  ? "本轮已全部掌握！"
                  : string.Join("\r\n\r\n",
                                a.Where(x => x.Weak)
                                  .Select(x => x.Word.Text + "（第" + x.Word.Page + "页）\r\n题目：" +
                                               x.Prompt + "\r\n全部意思：" +
                                               string.Join("；", x.Word.Meanings.Select(m => m.ToString())) +
                                               "\r\n输入：" + string.Join(" → ", x.Attempts) + "　提示：" +
                                               string.Join("、", x.Hints) + (x.Skipped ? "　已跳过" : "")));
    var bar = MainForm.Bar();
    bar.Dock = DockStyle.Bottom;
    if (allowRepeat && weak > 0)
      bar.Controls.Add(MainForm.Btn("加深记忆（" + weak + " 词）", () => {
        Repeat = true;
        Close();
      }));
    bar.Controls.Add(MainForm.Btn("关闭", () => Close()));
    Controls.Add(list);
    Controls.Add(summary);
    Controls.Add(bar);
  }
}

static class Tests {

  public static void Run() {
    int n = 0;
    Action<bool> check = b => {
      if (!b)
        throw new Exception("测试失败 " + n);
      n++;
    };
    check(Rules.Matches("  LOOK   UP ", "look up"));
    check(!Rules.Matches("lookup", "look up"));
    check(!Rules.Matches("well known", "well-known"));
    check(!Rules.Matches("well--known", "well-known"));
    var words = ImportForm.Parse("pulse | 25 | n.: 脉搏 | v.: 搏动，跳动；洋溢着");
    check(words[0].Meanings.Count == 3);
    check(words[0].Meanings[1].Text == "搏动，跳动");
    check(words[0].Page == 25);
    bool invalid = false;
    try {
      ImportForm.Parse("a | 1.5 | n.: 意思");
    } catch {
      invalid = true;
    }
    check(invalid);
    var a = new Answer { Word = words[0], Correct = true };
    a.Attempts.Add("pulse");
    check(!a.Weak);
    a.Hints.Add("首字母");
    check(a.Weak);
    a.Hints.Clear();
    a.Attempts.Insert(0, "wrong");
    check(a.Weak);
    a.Correct = false;
    a.Skipped = true;
    check(a.Weak);
    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-results.txt"),
                      "通过 " + n + " 项核心规则测试");
  }
}

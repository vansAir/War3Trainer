using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace War3Trainer
{
    public partial class MainForm : Form
    {
        private GameContext _currentGameContext;
        private GameTrainer _mainTrainer;
        private const string ImmortalButtonText = "我欲成仙,快乐齐天";
        private const string AttackRateCaption = "攻击频率比";
        private const string AttackAcquireRangeCaption = "主动攻击范围";
        private const string AttackOneRangeCaption = "攻击① - 范围";
        private const string AttackOneCooldownCaption = "攻击① - 间隔";
        private readonly Dictionary<string, UInt32> _immortalAddresses = new Dictionary<string, UInt32>();
        private System.Windows.Forms.Timer _immortalTimer;
        private Button _immortalButton;

        public MainForm()
        {
            InitializeComponent();
            ConfigureImmortalInterface();
        }

        private void FrmMain_Load(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.EnterDebugMode();
            }
            catch
            {
                ReportEnterDebugFailure();
                if (_immortalButton != null)
                    _immortalButton.Enabled = false;
                return;
            }

            SetImmortalStatus("等待启动");
        }

        private void ConfigureImmortalInterface()
        {
            SuspendLayout();

            toolContainer.Visible = false;
            splitMain.Visible = false;
            Controls.Clear();

            _immortalButton = new Button();
            _immortalButton.Name = "cmdImmortal";
            _immortalButton.Text = ImmortalButtonText;
            _immortalButton.Dock = DockStyle.Fill;
            _immortalButton.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold, GraphicsUnit.Point, ((byte)(134)));
            _immortalButton.Click += cmdImmortal_Click;
            Controls.Add(_immortalButton);

            ClientSize = new Size(280, 80);
            MinimumSize = new Size(296, 119);
            MaximumSize = new Size(296, 119);
            Text = ImmortalButtonText;

            _immortalTimer = new System.Windows.Forms.Timer(components);
            _immortalTimer.Interval = 1000;
            _immortalTimer.Tick += ImmortalTimer_Tick;

            ResumeLayout(false);
        }

        /************************************************************************/
        /* Main functions                                                       */
        /************************************************************************/
        private void FindGame()
        {
            bool isRecognized = false;
            try
            {
                _currentGameContext = GameContext.FindGameRunning("war3", "game.dll");
                if (_currentGameContext == null)
                {
                    // netease war3 platform(dz.163.com)
                    _currentGameContext = GameContext.FindGameRunning("dzwar3", "game.dll");
                }
                if (_currentGameContext != null)
                {
                    // Game online
                    ReportVersionOk(_currentGameContext.ProcessId, _currentGameContext.ProcessVersion);

                    // Get a new trainer
                    GetAllObject();

                    isRecognized = true;
                }
                else
                {
                    // Game offline
                    ReportNoGameFoundFailure();
                }
            }
            catch (UnkonwnGameVersionExpection ex)
            {
                // Unknown game version
                _currentGameContext = null;
                ReportVersionFailure(ex.ProcessId, ex.GameVersion);
            }
            catch (WindowsApi.BadProcessIdException ex)
            {
                this._currentGameContext = null;
                ReportProcessIdFailure(ex.ProcessId);
            }
            catch (Exception ex)
            {
                // Why here?
                _currentGameContext = null;
                ReportUnknownFailure(ex.Message);
            }

            // Enable buttons
            if (isRecognized)
            {
                viewFunctions.Enabled = true;
                viewData.Enabled = true;
                toolStripButton2.Enabled = true;
                toolStripButton1.Enabled = true;
            }
            else
            {
                viewFunctions.Enabled = false;
                viewData.Enabled = false;
                toolStripButton2.Enabled = false;
                toolStripButton1.Enabled = false;
            }
        }

        private void GetAllObject()
        {
            // Check paramters
            if (_currentGameContext == null)
                return;

            // Get a new trainer
            _mainTrainer = new GameTrainer(_currentGameContext);

            // Create function tree
            viewFunctions.Nodes.Clear();
            foreach (ITrainerNode currentFunction in _mainTrainer.GetFunctionList())
            {
                if (currentFunction.NodeType == TrainerNodeType.Introduction)
                    continue;

                TreeNode[] parentNodes = viewFunctions.Nodes.Find(currentFunction.ParentIndex.ToString(), true);
                TreeNodeCollection parentTree;
                if (parentNodes.Length < 1)
                    parentTree = viewFunctions.Nodes;
                else
                    parentTree = parentNodes[0].Nodes;

                parentTree.Add(
                    currentFunction.NodeIndex.ToString(),
                    currentFunction.NodeTypeName)
                    .Tag = currentFunction;
            }
            viewFunctions.ExpandAll();

            // Switch to page 1
            TreeNode[] introductionNodes = viewFunctions.Nodes.Find("1", true);
            if (introductionNodes.Length > 0)
            {
                viewFunctions.SelectedNode = introductionNodes[0];
                SelectFunction(introductionNodes[0]);
            }
            UpdateNodeIcons(viewFunctions.Nodes);
        }
        private void UpdateNodeIcons(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Nodes.Count > 0)
                {
                    node.ImageIndex = 0;
                    node.SelectedImageIndex = 0;
                }
                else
                {
                    node.ImageIndex = 1;
                    node.SelectedImageIndex = 1;
                }
                if (node.Nodes.Count > 0)
                {
                    UpdateNodeIcons(node.Nodes);
                }
            }
        }
        // Re-query specific tree-node by FunctionListNode
        private void RefreshSelectedObject(ITrainerNode currentFunction)
        {
            TreeNode[] currentNodes = viewFunctions.Nodes.Find(currentFunction.NodeIndex.ToString(), true);
            TreeNode currentTree;
            if (currentNodes.Length < 1)
                return;
            else
                currentTree = currentNodes[0];

            currentTree.Text = currentFunction.NodeTypeName;
        }

        private void SelectFunction(TreeNode functionNode)
        {
            if (functionNode == null)
                return;
            ITrainerNode node = functionNode.Tag as ITrainerNode;
            if (node == null)
                return;

            FillAddressList(node.NodeIndex);
        }

        private void FillAddressList(int functionNodeId)
        {
            // To set the right window
            viewData.Items.Clear();
            foreach (IAddressNode addressLine in _mainTrainer.GetAddressList())
            {
                if (addressLine.ParentIndex != functionNodeId)
                    continue;

                viewData.Items.Add(new ListViewItem(
                    new string[]
                    {
                        addressLine.Caption,    // Caption
                        "",                     // Original value
                        ""                      // Modified value
                    }));
                viewData.Items[viewData.Items.Count - 1].Tag = addressLine;
            }

            // To get memory content
            using (WindowsApi.ProcessMemory mem = new WindowsApi.ProcessMemory(_currentGameContext.ProcessId))
            {
                foreach (ListViewItem currentItem in viewData.Items)
                {
                    IAddressNode addressLine = currentItem.Tag as IAddressNode;
                    if (addressLine == null)
                        continue;

                    Object itemValue;
                    switch (addressLine.ValueType)
                    {
                        case AddressListValueType.Integer:
                            itemValue = mem.ReadInt32((IntPtr)addressLine.Address)
                                / addressLine.ValueScale;
                            break;
                        case AddressListValueType.Float:
                            itemValue = mem.ReadFloat((IntPtr)addressLine.Address)
                                / addressLine.ValueScale;
                            break;
                        case AddressListValueType.Char4:
                            itemValue = mem.ReadChar4((IntPtr)addressLine.Address);
                            break;
                        default:
                            itemValue = "";
                            break;
                    }
                    currentItem.SubItems[1].Text = itemValue.ToString();
                    currentItem.ImageIndex = 2;
                }
            }
        }

        // To apply the modifications
        private void ApplyModify()
        {
            using (WindowsApi.ProcessMemory mem = new WindowsApi.ProcessMemory(_currentGameContext.ProcessId))
            {
                foreach (ListViewItem currentItem in viewData.Items)
                {
                    string itemValueString = currentItem.SubItems[2].Text;
                    if (String.IsNullOrEmpty(itemValueString))
                    {
                        // Not modified
                        continue;
                    }

                    IAddressNode addressLine = currentItem.Tag as IAddressNode;
                    if (addressLine == null)
                        continue;

                    switch (addressLine.ValueType)
                    {
                        case AddressListValueType.Integer:
                            Int32 intValue;
                            if (!Int32.TryParse(itemValueString, out intValue))
                                intValue = 0;
                            intValue = unchecked(intValue * addressLine.ValueScale);
                            mem.WriteInt32((IntPtr)addressLine.Address, intValue);
                            break;
                        case AddressListValueType.Float:
                            float floatValue;
                            if (!float.TryParse(itemValueString, out floatValue))
                                floatValue = 0;
                            floatValue = unchecked(floatValue * addressLine.ValueScale);
                            mem.WriteFloat((IntPtr)addressLine.Address, floatValue);
                            break;
                        case AddressListValueType.Char4:
                            mem.WriteChar4((IntPtr)addressLine.Address, itemValueString);
                            break;
                    }
                    currentItem.SubItems[2].Text = "";
                }
            }
        }

        private void cmdImmortal_Click(object sender, EventArgs e)
        {
            try
            {
                StartImmortalMode();
            }
            catch (WindowsApi.BadProcessIdException ex)
            {
                StopImmortalMode();
                ReportProcessIdFailure(ex.ProcessId);
            }
            catch (Exception ex)
            {
                StopImmortalMode();
                ReportUnknownFailure(ex.Message);
            }
        }

        private void StartImmortalMode()
        {
            StopImmortalMode();

            FindGame();
            if (_currentGameContext == null || _mainTrainer == null)
                return;

            ITrainerNode attackNode = GetFirstSelectedUnitAttackNode();
            if (attackNode == null)
            {
                SetImmortalStatus("请先在游戏中选中至少一个单位");
                return;
            }

            RememberImmortalAddresses(attackNode.NodeIndex);
            if (!HasAllImmortalAddresses())
            {
                StopImmortalMode();
                SetImmortalStatus("未找到完整战斗属性地址");
                return;
            }

            ForceWriteImmortalValues();
            _immortalTimer.Start();
            SetImmortalStatus("已锁定第一个选中单位，每秒强制写入");
        }

        private void StopImmortalMode()
        {
            if (_immortalTimer != null)
                _immortalTimer.Stop();
            _immortalAddresses.Clear();
        }

        private ITrainerNode GetFirstSelectedUnitAttackNode()
        {
            ITrainerNode selectedUnitsNode = null;
            foreach (ITrainerNode node in _mainTrainer.GetFunctionList())
            {
                if (node.NodeType == TrainerNodeType.AllSelectedUnits)
                {
                    selectedUnitsNode = node;
                    break;
                }
            }

            if (selectedUnitsNode == null)
                return null;

            ITrainerNode firstUnitNode = null;
            foreach (ITrainerNode node in _mainTrainer.GetFunctionList())
            {
                if (node.NodeType == TrainerNodeType.OneSelectedUnit
                    && node.ParentIndex == selectedUnitsNode.NodeIndex)
                {
                    firstUnitNode = node;
                    break;
                }
            }

            if (firstUnitNode == null)
                return null;

            foreach (ITrainerNode node in _mainTrainer.GetFunctionList())
            {
                if (node.NodeType == TrainerNodeType.AttackAttributes
                    && node.ParentIndex == firstUnitNode.NodeIndex)
                {
                    SelectFunctionByNodeIndex(node.NodeIndex);
                    return node;
                }
            }

            return null;
        }

        private void SelectFunctionByNodeIndex(int nodeIndex)
        {
            TreeNode[] nodes = viewFunctions.Nodes.Find(nodeIndex.ToString(), true);
            if (nodes.Length < 1)
                return;

            viewFunctions.SelectedNode = nodes[0];
            SelectFunction(nodes[0]);
        }

        private void RememberImmortalAddresses(int attackNodeIndex)
        {
            _immortalAddresses.Clear();
            foreach (IAddressNode addressLine in _mainTrainer.GetAddressList())
            {
                if (addressLine.ParentIndex != attackNodeIndex)
                    continue;
                if (addressLine.ValueType != AddressListValueType.Float)
                    continue;

                switch (addressLine.Caption)
                {
                    case AttackRateCaption:
                    case AttackAcquireRangeCaption:
                    case AttackOneRangeCaption:
                    case AttackOneCooldownCaption:
                        _immortalAddresses[addressLine.Caption] = addressLine.Address;
                        break;
                }
            }
        }

        private bool HasAllImmortalAddresses()
        {
            return _immortalAddresses.ContainsKey(AttackRateCaption)
                && _immortalAddresses.ContainsKey(AttackAcquireRangeCaption)
                && _immortalAddresses.ContainsKey(AttackOneRangeCaption)
                && _immortalAddresses.ContainsKey(AttackOneCooldownCaption);
        }

        private void ForceWriteImmortalValues()
        {
            if (_currentGameContext == null || !HasAllImmortalAddresses())
                return;

            using (WindowsApi.ProcessMemory mem = new WindowsApi.ProcessMemory(_currentGameContext.ProcessId))
            {
                WriteImmortalFloat(mem, AttackRateCaption, 5.0f);
                WriteImmortalFloat(mem, AttackAcquireRangeCaption, 4000.0f);
                WriteImmortalFloat(mem, AttackOneRangeCaption, 4000.0f);
                WriteImmortalFloat(mem, AttackOneCooldownCaption, 0.2f);
            }
        }

        private void WriteImmortalFloat(WindowsApi.ProcessMemory mem, string caption, float value)
        {
            mem.WriteFloat((IntPtr)_immortalAddresses[caption], value);
        }

        private void ImmortalTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                ForceWriteImmortalValues();
            }
            catch (WindowsApi.BadProcessIdException ex)
            {
                StopImmortalMode();
                ReportProcessIdFailure(ex.ProcessId);
            }
            catch (Exception ex)
            {
                StopImmortalMode();
                ReportUnknownFailure(ex.Message);
            }
        }

        /************************************************************************/
        /* Exception UI                                                         */
        /************************************************************************/
        private void SetImmortalStatus(string message)
        {
            labGameScanState.Text = message;
            Text = ImmortalButtonText + " - " + message;
        }

        private void ReportEnterDebugFailure()
        {
            SetImmortalStatus("请以管理员身份运行");
        }

        private void ReportNoGameFoundFailure()
        {
            SetImmortalStatus("游戏未运行，运行游戏后单击按钮");
        }

        private void ReportUnknownFailure(string message)
        {
            SetImmortalStatus("发生未知错误：" + message);
        }

        private void ReportProcessIdFailure(int processId)
        {
            SetImmortalStatus("错误的进程ID："
                + processId.ToString());
        }

        private void ReportVersionFailure(int processId, string version)
        {
            SetImmortalStatus("游戏已运行，但版本（"
                + version
                + "）不被支持");
        }

        private void ReportVersionOk(int processId, string version)
        {
            SetImmortalStatus("游戏已运行("
                + processId.ToString()
                + ")，版本："
                + version
                + "（支持）");
        }

        /************************************************************************/
        /* GUI                                                                  */
        /************************************************************************/
        private void MenuHelpAbout_Click(object sender, EventArgs e)
        {
            DialogResult r = MessageBox.Show("Warcraft III 内存修改器"
                 + Application.ProductVersion + System.Environment.NewLine
                 + System.Environment.NewLine
                 + "暴徒修改：https://github.com/Hooliby/War3Trainer" + System.Environment.NewLine
                 + "",
                 "War3Trainer",
                 MessageBoxButtons.OKCancel,
                 MessageBoxIcon.Information);

            if (r == DialogResult.OK)
            {
                try
                {
                    Process.Start("https://github.com/Hooliby/War3Trainer");
                }
                catch { }
            }

        }

        private void MenuFileExit_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void cmdScanGame_Click(object sender, EventArgs e)
        {
            FindGame();
        }

        private void viewFunctions_BeforeSelect(object sender, TreeViewCancelEventArgs e)
        {
            // Check whether modification is not saved
            bool isSaved = true;
            foreach (ListViewItem currentItem in viewData.Items)
            {
                if (!String.IsNullOrEmpty(currentItem.SubItems[2].Text))
                {
                    isSaved = false;
                    break;
                }
            }

            // Save all if not saved
            if (!isSaved)
            {
                toolStripButton1_Click(this, null);
            }

            // Select another function
            try
            {
                SelectFunction(e.Node);
            }
            catch (WindowsApi.BadProcessIdException ex)
            {
                ReportProcessIdFailure(ex.ProcessId);
            }
        }

        private enum RightFunction
        {
            Empty,
            Introduction,
            EditTable,
        }

        //////////////////////////////////////////////////////////////////////////       
        // Make the ListView editable
        private void ReplaceInputTextbox()
        {
            if (viewData.SelectedItems.Count < 1)
                return;
            ListViewItem currentItem = viewData.SelectedItems[0];

            txtInput.Location = new Point(
                viewData.Columns[0].Width + viewData.Columns[1].Width,
                currentItem.Position.Y + 2);
            txtInput.Width = viewData.Columns[2].Width + 2;
        }

        private void viewData_KeyPress(object sender, KeyPressEventArgs e)
        {
            switch ((Keys)e.KeyChar)
            {
                case Keys.Enter:
                    viewData_MouseUp(sender, null);
                    e.Handled = true;
                    break;
            }
        }
        private void viewData_MouseUp(object sender, MouseEventArgs e)
        {
            //Get item
            if (viewData.SelectedItems.Count < 1) return;
            ListViewItem currentItem = viewData.SelectedItems[0];

            //Determine the content of edit box
            ReplaceInputTextbox();
            txtInput.Tag = currentItem;

            int textToEdit = string.IsNullOrEmpty(currentItem.SubItems[2].Text) ? 1 : 2;
            string originalText = currentItem.SubItems[textToEdit].Text;
            string itemName = currentItem.SubItems[0].Text;

            txtInput.Text = CalculateInputValue(itemName, originalText);
            //txtInput.Text = currentItem.SubItems[textToEdit].Text;

            //Enable editing
            txtInput.Visible = true;
            txtInput.Focus();
            txtInput.Select(0, 0);  // Cancel select all
        }

        private string CalculateInputValue(string itemName, string originalText)
        {
            if (itemName == "攻击① - 间隔") return "0.01";
            if (itemName.Contains("金币") || itemName.Contains("木材")) return "900000";
            if (itemName.Contains("最大人口")) return "100";

            int multiplier = xToolStripMenuItem1.Checked ? 2 :
                xToolStripMenuItem2.Checked ? 3 :
                toolStripMenuItem7.Checked ? 4 :
                xToolStripMenuItem3.Checked ? 5 : 1;

            if (multiplier > 1 && double.TryParse(originalText, out double val))
            {
                return ((int)Math.Round(val, MidpointRounding.AwayFromZero) * multiplier).ToString();
            }

            return originalText;
        }


        private void viewData_ColumnWidthChanging(object sender, ColumnWidthChangingEventArgs e)
        {
            ReplaceInputTextbox();

        }

        private void viewData_Scrolling(object sender, EventArgs e)
        {
            viewData.Focus();
        }

        private void txtInput_Leave(object sender, EventArgs e)
        {
            txtInput.Visible = false;
            ListViewItem currentItem = txtInput.Tag as ListViewItem;
            if (currentItem == null)
                return;

            if (currentItem.SubItems[1].Text != txtInput.Text)
                currentItem.SubItems[2].Text = txtInput.Text;
            else
                currentItem.SubItems[2].Text = "";
        }

        private void txtInput_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Enter:
                    CommitEditAndMoveNext(sender, 1);
                    e.Handled = true;
                    break;
                case Keys.Up:
                    CommitEditAndMoveNext(sender, -1);
                    e.Handled = true;
                    break;
                case Keys.Down:
                    CommitEditAndMoveNext(sender, 1);
                    e.Handled = true;
                    break;
                case Keys.Escape:
                    DiscardEdit(sender);
                    e.Handled = true;
                    break;
            }
        }

        private void DiscardEdit(object editBox)
        {
            // Roll back content of the edit box
            viewData_MouseUp(editBox, null);

            // Hide edit box
            txtInput_Leave(editBox, null);

            // Restore focus
            viewData.Focus();
        }

        private void CommitEditAndMoveNext(object editBox, int delta)
        {
            // Commit
            txtInput_Leave(editBox, null);

            // Move to another line
            viewData.Focus();
            if (viewData.SelectedItems.Count > 0)
            {
                int nextIndex = viewData.SelectedItems[0].Index + delta;
                if (nextIndex < viewData.Items.Count &&
                    nextIndex >= 0)
                {
                    viewData.Items[nextIndex].Selected = true;
                    viewData.Items[nextIndex].Focused = true;
                    viewData.Items[nextIndex].EnsureVisible();
                }
                viewData_MouseUp(editBox, null);
            }
        }

        /************************************************************************/
        /* Debug                                                                */
        /************************************************************************/
        private void menuDebug1_Click(object sender, EventArgs e)
        {
            string strIndex = Microsoft.VisualBasic.Interaction.InputBox(
                "nIndex = 0x?",
                "War3Common.ReadFromGameMemory(nIndex)",
                "0", -1, -1);
            if (String.IsNullOrEmpty(strIndex))
                return;

            Int32 nIndex;
            if (!Int32.TryParse(
                strIndex,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.NumberFormatInfo.InvariantInfo,
                out nIndex))
            {
                nIndex = 0;
            }

            try
            {
                UInt32 result = 0;
                using (WindowsApi.ProcessMemory mem = new WindowsApi.ProcessMemory(_currentGameContext.ProcessId))
                {
                    NewChildrenEventArgs args = new NewChildrenEventArgs();
                    War3Common.GetGameMemory(
                        _currentGameContext, ref args);
                    result = War3Common.ReadFromGameMemory(
                        mem, _currentGameContext, args,
                        nIndex);
                }
                MessageBox.Show(
                    "0x" + result.ToString("X"),
                    "War3Common.ReadFromGameMemory(0x" + strIndex + ")");
            }
            catch (WindowsApi.BadProcessIdException ex)
            {
                ReportProcessIdFailure(ex.ProcessId);
            }
        }

        private void 启用ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.TopMost = 启用ToolStripMenuItem.Checked;
        }

        private void 解除修改限制ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (解除修改限制ToolStripMenuItem == null || txtInput == null)
                return;
            txtInput.MaxLength = 解除修改限制ToolStripMenuItem.Checked ? 35 : 7;
        }

        private void viewFunctions_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyData == Keys.F5)
            {
                toolStripButton2_Click(sender, null);
                e.Handled = true;
            }

        }

        private void viewData_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyData == Keys.F6)
            {
                toolStripButton1_Click(sender, null);
                e.Handled = true;
            }
        }

        private void toolStripButton1_Click(object sender, EventArgs e)
        {
            try
            {
                ApplyModify();

                // Refresh left
                TreeNode selectedNode = viewFunctions.SelectedNode;
                if (selectedNode == null)
                    return;

                ITrainerNode functionNode = selectedNode.Tag as ITrainerNode;
                if (functionNode != null)
                    RefreshSelectedObject(functionNode);

                // Refresh right
                SelectFunction(selectedNode);
            }
            catch (WindowsApi.BadProcessIdException ex)
            {
                ReportProcessIdFailure(ex.ProcessId);
            }
        }

        private void toolStripButton2_Click(object sender, EventArgs e)
        {
            try
            {
                GetAllObject();
            }
            catch (WindowsApi.BadProcessIdException ex)
            {
                ReportProcessIdFailure(ex.ProcessId);
            }
        }

        private void GroupedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem currentItem = sender as ToolStripMenuItem;
            if (currentItem == null) return;

            ToolStripMenuItem[] allItems = new ToolStripMenuItem[]
            {
                xToolStripMenuItem,
                xToolStripMenuItem1,
                xToolStripMenuItem2,
                toolStripMenuItem7,
                xToolStripMenuItem3
            };

            foreach (var item in allItems)
            {
                item.Checked = (item == currentItem);
            }
        }
    }
}

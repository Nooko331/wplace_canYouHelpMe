using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace WplaceColorWatch
{

partial class Form1
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;
    private System.Windows.Forms.Panel panelLeft;
    private System.Windows.Forms.Panel panelRight;
    private System.Windows.Forms.Label labelRec;
    private System.Windows.Forms.Label RangeRecord;
    private System.Windows.Forms.CheckBox checkShowRange;
    private System.Windows.Forms.Button btnRange;
    private System.Windows.Forms.Label labelCores;
    private System.Windows.Forms.TextBox textCores;
    private System.Windows.Forms.Button btnAutoCores;
    private System.Windows.Forms.Timer updateTimer;
    private System.Windows.Forms.Button btnAutoFillAll;
    private System.Windows.Forms.Label labelAutoAll;
    private System.Windows.Forms.ProgressBar progressAutoAll;
    private System.Windows.Forms.Label labelAutoAllValue;
    private System.Windows.Forms.Button btnToggleLayout;
    private System.Windows.Forms.LinkLabel linkGithubOrUpdate;
    private System.Windows.Forms.Label labelCurrentVersion;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
            this.components = new System.ComponentModel.Container();
            this.panelLeft = new System.Windows.Forms.Panel();
            this.labelRec = new System.Windows.Forms.Label();
            this.panelRight = new System.Windows.Forms.Panel();
            this.RangeRecord = new System.Windows.Forms.Label();
            this.checkShowRange = new System.Windows.Forms.CheckBox();
            this.btnRange = new System.Windows.Forms.Button();
            this.labelCores = new System.Windows.Forms.Label();
            this.textCores = new System.Windows.Forms.TextBox();
            this.btnAutoCores = new System.Windows.Forms.Button();
            this.updateTimer = new System.Windows.Forms.Timer(this.components);
            this.color1 = new System.Windows.Forms.Label();
            this.color2 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.TheRange = new System.Windows.Forms.Label();
            this.label10 = new System.Windows.Forms.Label();
            this.ScanStep = new System.Windows.Forms.TextBox();
            this.label7 = new System.Windows.Forms.Label();
            this.label8 = new System.Windows.Forms.Label();
            this.btnAutoFillAll = new System.Windows.Forms.Button();
            this.labelAutoAll = new System.Windows.Forms.Label();
            this.progressAutoAll = new System.Windows.Forms.ProgressBar();
            this.labelAutoAllValue = new System.Windows.Forms.Label();
            this.btnToggleLayout = new System.Windows.Forms.Button();
            this.linkGithubOrUpdate = new System.Windows.Forms.LinkLabel();
            this.labelCurrentVersion = new System.Windows.Forms.Label();
            this.panelLeft.SuspendLayout();
            this.SuspendLayout();
            // 
            // panelLeft
            // 
            this.panelLeft.BackColor = System.Drawing.Color.Black;
            this.panelLeft.Controls.Add(this.labelRec);
            this.panelLeft.Location = new System.Drawing.Point(12, 62);
            this.panelLeft.Name = "panelLeft";
            this.panelLeft.Size = new System.Drawing.Size(108, 50);
            this.panelLeft.TabIndex = 16;
            // 
            // labelRec
            // 
            this.labelRec.AutoSize = true;
            this.labelRec.BackColor = System.Drawing.Color.Transparent;
            this.labelRec.ForeColor = System.Drawing.Color.White;
            this.labelRec.Location = new System.Drawing.Point(0, 0);
            this.labelRec.Name = "labelRec";
            this.labelRec.Size = new System.Drawing.Size(37, 20);
            this.labelRec.TabIndex = 14;
            this.labelRec.Text = "REC";
            // 
            // panelRight
            // 
            this.panelRight.BackColor = System.Drawing.Color.Black;
            this.panelRight.Location = new System.Drawing.Point(133, 62);
            this.panelRight.Name = "panelRight";
            this.panelRight.Size = new System.Drawing.Size(106, 50);
            this.panelRight.TabIndex = 15;
            // 
            // RangeRecord
            // 
            this.RangeRecord.AutoSize = true;
            this.RangeRecord.ForeColor = System.Drawing.Color.Red;
            this.RangeRecord.Location = new System.Drawing.Point(12, 311);
            this.RangeRecord.Name = "RangeRecord";
            this.RangeRecord.Size = new System.Drawing.Size(84, 20);
            this.RangeRecord.TabIndex = 11;
            this.RangeRecord.Text = "范围未记录";
            // 
            // checkShowRange
            //
            this.checkShowRange.AutoSize = true;
            this.checkShowRange.Location = new System.Drawing.Point(12, 335);
            this.checkShowRange.Name = "checkShowRange";
            this.checkShowRange.Size = new System.Drawing.Size(120, 24);
            this.checkShowRange.TabIndex = 28;
            this.checkShowRange.Text = "显示框选范围";
            this.checkShowRange.UseVisualStyleBackColor = true;
            //
            // btnRange
            //
            this.btnRange.Location = new System.Drawing.Point(12, 273);
            this.btnRange.Name = "btnRange";
            this.btnRange.Size = new System.Drawing.Size(122, 26);
            this.btnRange.TabIndex = 10;
            this.btnRange.Text = "划取检测范围";
            this.btnRange.UseVisualStyleBackColor = true;
            this.btnRange.Click += new System.EventHandler(this.btnRange_Click);
            // 
            // labelCores
            // 
            this.labelCores.AutoSize = true;
            this.labelCores.Location = new System.Drawing.Point(12, 160);
            this.labelCores.Name = "labelCores";
            this.labelCores.Size = new System.Drawing.Size(99, 20);
            this.labelCores.TabIndex = 2;
            this.labelCores.Text = "调用CPU数量";
            // 
            // textCores
            // 
            this.textCores.Location = new System.Drawing.Point(119, 157);
            this.textCores.Name = "textCores";
            this.textCores.Size = new System.Drawing.Size(40, 27);
            this.textCores.TabIndex = 1;
            // 
            // btnAutoCores
            // 
            this.btnAutoCores.Location = new System.Drawing.Point(10, 198);
            this.btnAutoCores.Name = "btnAutoCores";
            this.btnAutoCores.Size = new System.Drawing.Size(142, 26);
            this.btnAutoCores.TabIndex = 0;
            this.btnAutoCores.Text = "自动决定CPU数量";
            this.btnAutoCores.UseVisualStyleBackColor = true;
            // 
            // updateTimer
            // 
            this.updateTimer.Interval = 50;
            // 
            // color1
            // 
            this.color1.AutoSize = true;
            this.color1.Location = new System.Drawing.Point(12, 39);
            this.color1.Name = "color1";
            this.color1.Size = new System.Drawing.Size(108, 20);
            this.color1.TabIndex = 17;
            this.color1.Text = "当前记录颜色1";
            // 
            // color2
            // 
            this.color2.AutoSize = true;
            this.color2.Location = new System.Drawing.Point(133, 39);
            this.color2.Name = "color2";
            this.color2.Size = new System.Drawing.Size(108, 20);
            this.color2.TabIndex = 18;
            this.color2.Text = "当前记录颜色2";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(245, 62);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(123, 20);
            this.label1.TabIndex = 20;
            this.label1.Text = "最好记录2种颜色";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(224, 273);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(144, 20);
            this.label2.TabIndex = 21;
            this.label2.Text = "划取检测范围（两种方式）";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(12, 9);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(356, 20);
            this.label3.TabIndex = 22;
            this.label3.Text = "按下A取色，取色前要进入wplace的选色、填色界面";
            this.label3.Click += new System.EventHandler(this.label3_Click);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(303, 157);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(63, 20);
            this.label4.TabIndex = 23;
            this.label4.Text = "默认是1";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(252, 198);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(114, 20);
            this.label5.TabIndex = 24;
            this.label5.Text = "CPU数量的一半";
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(12, 127);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(361, 20);
            this.label6.TabIndex = 25;
            this.label6.Text = "——————————————————————";
            // 
            // TheRange
            // 
            this.TheRange.AutoSize = true;
            this.TheRange.Location = new System.Drawing.Point(113, 311);
            this.TheRange.Name = "TheRange";
            this.TheRange.Size = new System.Drawing.Size(18, 20);
            this.TheRange.TabIndex = 29;
            this.TheRange.Text = "0";
            // 
            // label10
            // 
            this.label10.AutoSize = true;
            this.label10.Location = new System.Drawing.Point(167, 341);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(201, 20);
            this.label10.TabIndex = 30;
            this.label10.Text = "填充过程中按ESC可停止填充";
            // 
            // ScanStep
            // 
            this.ScanStep.Location = new System.Drawing.Point(89, 231);
            this.ScanStep.Name = "ScanStep";
            this.ScanStep.Size = new System.Drawing.Size(69, 27);
            this.ScanStep.TabIndex = 31;
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(14, 234);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(69, 20);
            this.label7.TabIndex = 32;
            this.label7.Text = "扫描步长";
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(176, 234);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(189, 20);
            this.label8.TabIndex = 33;
            this.label8.Text = "每间隔多少像素点进行扫描";
            this.label8.Click += new System.EventHandler(this.label8_Click);
            // 
            // btnAutoFillAll
            // 
            this.btnAutoFillAll.Location = new System.Drawing.Point(12, 520);
            this.btnAutoFillAll.Name = "btnAutoFillAll";
            this.btnAutoFillAll.Size = new System.Drawing.Size(160, 26);
            this.btnAutoFillAll.TabIndex = 34;
            this.btnAutoFillAll.Text = "全自动检测及填充";
            this.btnAutoFillAll.UseVisualStyleBackColor = true;
            // 
            // labelAutoAll
            // 
            this.labelAutoAll.AutoSize = true;
            this.labelAutoAll.Location = new System.Drawing.Point(17, 560);
            this.labelAutoAll.Name = "labelAutoAll";
            this.labelAutoAll.Size = new System.Drawing.Size(114, 20);
            this.labelAutoAll.TabIndex = 35;
            this.labelAutoAll.Text = "全自动填充进度";
            // 
            // progressAutoAll
            // 
            this.progressAutoAll.Location = new System.Drawing.Point(12, 585);
            this.progressAutoAll.Name = "progressAutoAll";
            this.progressAutoAll.Size = new System.Drawing.Size(351, 28);
            this.progressAutoAll.TabIndex = 36;
            // 
            // labelAutoAllValue
            // 
            this.labelAutoAllValue.AutoSize = true;
            this.labelAutoAllValue.Location = new System.Drawing.Point(160, 560);
            this.labelAutoAllValue.Name = "labelAutoAllValue";
            this.labelAutoAllValue.Size = new System.Drawing.Size(41, 20);
            this.labelAutoAllValue.TabIndex = 37;
            this.labelAutoAllValue.Text = "0 / 0";
            // 
            // btnToggleLayout
            // 
            this.btnToggleLayout.Location = new System.Drawing.Point(178, 520);
            this.btnToggleLayout.Name = "btnToggleLayout";
            this.btnToggleLayout.Size = new System.Drawing.Size(185, 26);
            this.btnToggleLayout.TabIndex = 38;
            this.btnToggleLayout.Text = "切换为横版布局";
            this.btnToggleLayout.UseVisualStyleBackColor = true;
            // 
            // linkGithubOrUpdate
            // 
            this.linkGithubOrUpdate.AutoSize = true;
            this.linkGithubOrUpdate.Location = new System.Drawing.Point(12, 609);
            this.linkGithubOrUpdate.Name = "linkGithubOrUpdate";
            this.linkGithubOrUpdate.Size = new System.Drawing.Size(115, 20);
            this.linkGithubOrUpdate.TabIndex = 39;
            this.linkGithubOrUpdate.TabStop = true;
            this.linkGithubOrUpdate.Text = "项目仓库（GitHub）";
            // 
            // labelCurrentVersion
            // 
            this.labelCurrentVersion.AutoSize = true;
            this.labelCurrentVersion.Location = new System.Drawing.Point(12, 589);
            this.labelCurrentVersion.Name = "labelCurrentVersion";
            this.labelCurrentVersion.Size = new System.Drawing.Size(121, 20);
            this.labelCurrentVersion.TabIndex = 40;
            this.labelCurrentVersion.Text = "当前版本: 1.0.1";
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(383, 630);
            this.Controls.Add(this.labelCurrentVersion);
            this.Controls.Add(this.linkGithubOrUpdate);
            this.Controls.Add(this.btnToggleLayout);
            this.Controls.Add(this.labelAutoAllValue);
            this.Controls.Add(this.progressAutoAll);
            this.Controls.Add(this.labelAutoAll);
            this.Controls.Add(this.btnAutoFillAll);
            this.Controls.Add(this.label8);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.ScanStep);
            this.Controls.Add(this.label10);
            this.Controls.Add(this.TheRange);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.color2);
            this.Controls.Add(this.color1);
            this.Controls.Add(this.btnAutoCores);
            this.Controls.Add(this.textCores);
            this.Controls.Add(this.labelCores);
            this.Controls.Add(this.btnRange);
            this.Controls.Add(this.checkShowRange);
            this.Controls.Add(this.RangeRecord);
            this.Controls.Add(this.panelRight);
            this.Controls.Add(this.panelLeft);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.Name = "Form1";
            this.Text = "color_status";
            this.TopMost = true;
            this.Load += new System.EventHandler(this.Form1_Load);
            this.panelLeft.ResumeLayout(false);
            this.panelLeft.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

    }

        #endregion

        private Label color1;
        private Label color2;
        private Label label1;
        private Label label2;
        private Label label3;
        private Label label4;
        private Label label5;
        private Label label6;
        private Label TheRange;
        private Label label10;
        private TextBox ScanStep;
        private Label label7;
        private Label label8;
    }
}


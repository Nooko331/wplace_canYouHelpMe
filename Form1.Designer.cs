namespace WplaceColorWatch;

partial class Form1
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;
    private System.Windows.Forms.Panel panelLeft;
    private System.Windows.Forms.Panel panelRight;
    private System.Windows.Forms.Label labelRec;
    private System.Windows.Forms.Label labelMatch;
    private System.Windows.Forms.Label labelX;
    private System.Windows.Forms.Label labelRange;
    private System.Windows.Forms.Button btnRange;
    private System.Windows.Forms.Button btnFill;
    private System.Windows.Forms.Label labelScan;
    private System.Windows.Forms.ProgressBar progressScan;
    private System.Windows.Forms.Label labelScanValue;
    private System.Windows.Forms.Label labelMatchProgress;
    private System.Windows.Forms.ProgressBar progressMatch;
    private System.Windows.Forms.Label labelMatchValue;
    private System.Windows.Forms.Label labelCores;
    private System.Windows.Forms.TextBox textCores;
    private System.Windows.Forms.Button btnAutoCores;
    private System.Windows.Forms.Timer updateTimer;

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
        this.panelRight = new System.Windows.Forms.Panel();
        this.labelRec = new System.Windows.Forms.Label();
        this.labelMatch = new System.Windows.Forms.Label();
        this.labelX = new System.Windows.Forms.Label();
        this.labelRange = new System.Windows.Forms.Label();
        this.btnRange = new System.Windows.Forms.Button();
        this.btnFill = new System.Windows.Forms.Button();
        this.labelScan = new System.Windows.Forms.Label();
        this.progressScan = new System.Windows.Forms.ProgressBar();
        this.labelScanValue = new System.Windows.Forms.Label();
        this.labelMatchProgress = new System.Windows.Forms.Label();
        this.progressMatch = new System.Windows.Forms.ProgressBar();
        this.labelMatchValue = new System.Windows.Forms.Label();
        this.labelCores = new System.Windows.Forms.Label();
        this.textCores = new System.Windows.Forms.TextBox();
        this.btnAutoCores = new System.Windows.Forms.Button();
        this.updateTimer = new System.Windows.Forms.Timer(this.components);
        this.SuspendLayout();
        // 
        // panelLeft
        // 
        this.panelLeft.BackColor = System.Drawing.Color.Black;
        this.panelLeft.Location = new System.Drawing.Point(10, 10);
        this.panelLeft.Name = "panelLeft";
        this.panelLeft.Size = new System.Drawing.Size(90, 50);
        // 
        // panelRight
        // 
        this.panelRight.BackColor = System.Drawing.Color.Black;
        this.panelRight.Location = new System.Drawing.Point(110, 10);
        this.panelRight.Name = "panelRight";
        this.panelRight.Size = new System.Drawing.Size(90, 50);
        // 
        // labelRec
        // 
        this.labelRec.AutoSize = true;
        this.labelRec.Location = new System.Drawing.Point(12, 14);
        this.labelRec.Name = "labelRec";
        this.labelRec.Size = new System.Drawing.Size(28, 15);
        this.labelRec.Text = "REC";
        this.labelRec.BackColor = System.Drawing.Color.Transparent;
        this.labelRec.ForeColor = System.Drawing.Color.White;
        // 
        // labelMatch
        // 
        this.labelMatch.AutoSize = true;
        this.labelMatch.Location = new System.Drawing.Point(10, 214);
        this.labelMatch.Name = "labelMatch";
        this.labelMatch.Size = new System.Drawing.Size(46, 15);
        this.labelMatch.Text = "MATCH";
        this.labelMatch.ForeColor = System.Drawing.Color.Green;
        this.labelMatch.Visible = false;
        // 
        // labelX
        // 
        this.labelX.AutoSize = true;
        this.labelX.Location = new System.Drawing.Point(120, 214);
        this.labelX.Name = "labelX";
        this.labelX.Size = new System.Drawing.Size(38, 15);
        this.labelX.Text = "S:OFF";
        this.labelX.ForeColor = System.Drawing.Color.Red;
        // 
        // labelRange
        // 
        this.labelRange.AutoSize = true;
        this.labelRange.Location = new System.Drawing.Point(166, 74);
        this.labelRange.Name = "labelRange";
        this.labelRange.Size = new System.Drawing.Size(34, 15);
        this.labelRange.Text = "R:--";
        // 
        // btnRange
        // 
        this.btnRange.Location = new System.Drawing.Point(10, 70);
        this.btnRange.Name = "btnRange";
        this.btnRange.Size = new System.Drawing.Size(70, 26);
        this.btnRange.Text = "RANGE";
        this.btnRange.UseVisualStyleBackColor = true;
        // 
        // btnFill
        // 
        this.btnFill.Location = new System.Drawing.Point(90, 70);
        this.btnFill.Name = "btnFill";
        this.btnFill.Size = new System.Drawing.Size(50, 26);
        this.btnFill.Text = "FILL";
        this.btnFill.UseVisualStyleBackColor = true;
        // 
        // labelCores
        // 
        this.labelCores.AutoSize = true;
        this.labelCores.Location = new System.Drawing.Point(10, 110);
        this.labelCores.Name = "labelCores";
        this.labelCores.Size = new System.Drawing.Size(43, 15);
        this.labelCores.Text = "CORES";
        // 
        // textCores
        // 
        this.textCores.Location = new System.Drawing.Point(58, 107);
        this.textCores.Name = "textCores";
        this.textCores.Size = new System.Drawing.Size(40, 23);
        // 
        // btnAutoCores
        // 
        this.btnAutoCores.Location = new System.Drawing.Point(104, 106);
        this.btnAutoCores.Name = "btnAutoCores";
        this.btnAutoCores.Size = new System.Drawing.Size(50, 24);
        this.btnAutoCores.Text = "AUTO";
        this.btnAutoCores.UseVisualStyleBackColor = true;
        // 
        // labelScan
        // 
        this.labelScan.AutoSize = true;
        this.labelScan.Location = new System.Drawing.Point(10, 138);
        this.labelScan.Name = "labelScan";
        this.labelScan.Size = new System.Drawing.Size(33, 15);
        this.labelScan.Text = "SCAN";
        // 
        // progressScan
        // 
        this.progressScan.Location = new System.Drawing.Point(10, 156);
        this.progressScan.Name = "progressScan";
        this.progressScan.Size = new System.Drawing.Size(190, 12);
        // 
        // labelScanValue
        // 
        this.labelScanValue.AutoSize = true;
        this.labelScanValue.Location = new System.Drawing.Point(60, 138);
        this.labelScanValue.Name = "labelScanValue";
        this.labelScanValue.Size = new System.Drawing.Size(40, 15);
        this.labelScanValue.Text = "0 / 0";
        // 
        // labelMatchProgress
        // 
        this.labelMatchProgress.AutoSize = true;
        this.labelMatchProgress.Location = new System.Drawing.Point(10, 176);
        this.labelMatchProgress.Name = "labelMatchProgress";
        this.labelMatchProgress.Size = new System.Drawing.Size(43, 15);
        this.labelMatchProgress.Text = "MATCH";
        // 
        // progressMatch
        // 
        this.progressMatch.Location = new System.Drawing.Point(10, 194);
        this.progressMatch.Name = "progressMatch";
        this.progressMatch.Size = new System.Drawing.Size(190, 12);
        // 
        // labelMatchValue
        // 
        this.labelMatchValue.AutoSize = true;
        this.labelMatchValue.Location = new System.Drawing.Point(60, 176);
        this.labelMatchValue.Name = "labelMatchValue";
        this.labelMatchValue.Size = new System.Drawing.Size(40, 15);
        this.labelMatchValue.Text = "0 / 0";
        // 
        // updateTimer
        // 
        this.updateTimer.Interval = 50;
        // 
        // Form1
        // 
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(212, 236);
        this.Controls.Add(this.btnAutoCores);
        this.Controls.Add(this.textCores);
        this.Controls.Add(this.labelCores);
        this.Controls.Add(this.labelMatchValue);
        this.Controls.Add(this.progressMatch);
        this.Controls.Add(this.labelMatchProgress);
        this.Controls.Add(this.labelScanValue);
        this.Controls.Add(this.progressScan);
        this.Controls.Add(this.labelScan);
        this.Controls.Add(this.btnFill);
        this.Controls.Add(this.btnRange);
        this.Controls.Add(this.labelRange);
        this.Controls.Add(this.labelX);
        this.Controls.Add(this.labelMatch);
        this.Controls.Add(this.labelRec);
        this.Controls.Add(this.panelRight);
        this.Controls.Add(this.panelLeft);
        this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
        this.Name = "Form1";
        this.Text = "color_status";
        this.TopMost = true;
        this.ResumeLayout(false);
        this.PerformLayout();
    }

    #endregion
}

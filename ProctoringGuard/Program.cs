using System;
using System.Windows.Forms;
using ProctoringGuard.UI;

// --- ProctoringGuard v5 Entry Point ---
// In .NET 8, [STAThread] is applied automatically to top-level code.

ApplicationConfiguration.Initialize();

try 
{
    // Launching the main dashboard
    Application.Run(new MainDashboard());
}
catch (Exception ex)
{
    // Diagnostic catch for environment or scaling issues
    MessageBox.Show($"Sentinel Boot Error: {ex.Message}", "Critical Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
}
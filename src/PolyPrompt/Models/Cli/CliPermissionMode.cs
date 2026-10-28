namespace PolyPrompt.Models
{
    /// <summary>
    /// What an agent harness may do to the machine it runs on. Each harness client maps these modes to the closest
    /// flags its CLI offers; the mapping is documented on each client. The default everywhere is
    /// <see cref="Restricted"/>, and no client ever defaults to a mode that bypasses approvals or sandboxing.
    /// </summary>
    public enum CliPermissionMode
    {
        /// <summary>
        /// The most conservative mode the harness offers: read-only tools only, and anything that would need an approval
        /// is denied because nobody is there to approve it. This is the default.
        /// </summary>
        Restricted,

        /// <summary>
        /// The harness may read the workspace and run read-only commands, but may not modify files.
        /// </summary>
        ReadOnly,

        /// <summary>
        /// The harness may edit files inside the working directory without asking. Commands outside the workspace or
        /// with network access stay restricted where the harness can enforce it.
        /// </summary>
        WorkspaceWrite,

        /// <summary>
        /// Approvals and sandboxing are bypassed. Only use this inside an environment that is already isolated.
        /// </summary>
        Unrestricted
    }
}

namespace PolyPrompt.Clients
{
    /// <summary>
    /// Construction settings for <see cref="OpenAiCompatibleClient"/>: the shared <see cref="ClientOptions"/> plus where
    /// the API key is placed on each request.
    /// </summary>
    public class OpenAiCompatibleClientOptions : ClientOptions
    {
        /// <summary>
        /// Where the API key goes. Default: <see cref="AuthPlacement.Bearer"/> (<c>Authorization: Bearer &lt;key&gt;</c>).
        /// Use <c>AuthPlacement.Header("api-key")</c> for Azure's v1 Chat Completions route.
        /// </summary>
        public AuthPlacement AuthPlacement { get; init; } = AuthPlacement.Bearer;
    }

    /// <summary>
    /// Where an <see cref="OpenAiCompatibleClient"/> sends its API key.
    /// </summary>
    public sealed class AuthPlacement
    {
        /// <summary>
        /// <c>Authorization: Bearer &lt;key&gt;</c>.
        /// </summary>
        public static AuthPlacement Bearer { get; } = new AuthPlacement(AuthPlacementKind.Bearer, "Authorization");

        /// <summary>
        /// How the key is placed.
        /// </summary>
        public AuthPlacementKind Kind { get; }

        /// <summary>
        /// Header or query parameter name. <c>Authorization</c> for <see cref="AuthPlacementKind.Bearer"/>.
        /// </summary>
        public string Name { get; }

        private AuthPlacement(AuthPlacementKind kind, string name)
        {
            Kind = kind;
            Name = name;
        }

        /// <summary>
        /// Send the key as the raw value of a header, for example <c>api-key</c>.
        /// </summary>
        /// <param name="name">Header name.</param>
        /// <returns>The placement.</returns>
        /// <exception cref="ArgumentException">Thrown when name is null, empty, or whitespace.</exception>
        public static AuthPlacement Header(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            return new AuthPlacement(AuthPlacementKind.Header, name.Trim());
        }

        /// <summary>
        /// Send the key as a query-string parameter, for example <c>key</c>.
        /// </summary>
        /// <param name="name">Query parameter name.</param>
        /// <returns>The placement.</returns>
        /// <exception cref="ArgumentException">Thrown when name is null, empty, or whitespace.</exception>
        public static AuthPlacement Query(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            return new AuthPlacement(AuthPlacementKind.Query, name.Trim());
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Kind == AuthPlacementKind.Bearer ? "Bearer" : Kind + "(" + Name + ")";
        }
    }

    /// <summary>
    /// Kinds of <see cref="AuthPlacement"/>.
    /// </summary>
    public enum AuthPlacementKind
    {
        /// <summary><c>Authorization: Bearer &lt;key&gt;</c>.</summary>
        Bearer,

        /// <summary>A named header holding the raw key.</summary>
        Header,

        /// <summary>A named query-string parameter.</summary>
        Query,
    }
}

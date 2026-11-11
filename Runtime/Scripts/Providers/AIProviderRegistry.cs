using System;
using System.Collections.Generic;
using VContainer;

namespace Conkist.GDK.AI.Providers
{
    /// <summary>
    /// Provider assemblies add themselves here at startup, so this assembly never references a provider.
    /// A provider assembly exists only while its scripting symbol is on.
    /// </summary>
    public static class AIProviderRegistry
    {
        private static readonly Dictionary<string, Action<IContainerBuilder>> Installers = new Dictionary<string, Action<IContainerBuilder>>();

        /// <summary>Adds or replaces the installer of one provider.</summary>
        public static void Register(string providerName, Action<IContainerBuilder> install)
        {
            Installers[providerName] = install;
        }

        internal static IEnumerable<Action<IContainerBuilder>> All => Installers.Values;
    }
}

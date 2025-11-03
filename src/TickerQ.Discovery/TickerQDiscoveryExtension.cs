using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;

namespace TickerQ.Discovery;

public static class TickerQDiscoveryExtension
{
    private const string GeneratedClassSuffix = "TickerQInstanceFactoryExtensions";
    
    /// <summary>
    /// Adds TickerQ discovery to automatically load assemblies with TickerQ functions.
    /// The assemblies will auto-initialize through their ModuleInitializer.
    /// </summary>
    public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddTickerQDiscovery<TTimeTicker, TCronTicker>(
        this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration, 
        params Assembly[] assemblies)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var assembliesToLoad = assemblies ?? [];
        
        // If no assemblies provided, discover all assemblies with TickerQ functions
        if (assembliesToLoad.Length == 0)
        {
            _ = DiscoverTickerQAssemblies();
        }
        else
        {
            // Validate provided assemblies have TickerQ functions
            _ = assembliesToLoad
                .Where(HasTickerQGeneratedClass)
                .ToArray();
        }
        
        // The assemblies are already loaded and their ModuleInitializers have run
        // Just return the configuration for chaining
        return tickerConfiguration;
    }
    
    /// <summary>
    /// Discovers assemblies in the current application that have TickerQ-generated classes.
    /// This method has better performance than checking all types for attributes.
    /// </summary>
    public static Assembly[] DiscoverTickerQAssemblies()
    {
        var assembliesWithTickerQ = new List<Assembly>();
        
        // Get all loaded assemblies in the current app domain
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location));
        
        foreach (var assembly in loadedAssemblies)
        {
            if (HasTickerQGeneratedClass(assembly))
            {
                assembliesWithTickerQ.Add(assembly);
            }
        }
        
        // Also check referenced assemblies that might not be loaded yet
        var referencedAssemblies = loadedAssemblies
            .SelectMany(a => a.GetReferencedAssemblies())
            .Distinct()
            .Where(name => !loadedAssemblies.Any(a => a.GetName().Name == name.Name));
        
        foreach (var assemblyName in referencedAssemblies)
        {
            try
            {
                // Loading the assembly will trigger its ModuleInitializer
                var assembly = Assembly.Load(assemblyName);
                if (HasTickerQGeneratedClass(assembly))
                {
                    assembliesWithTickerQ.Add(assembly);
                }
            }
            catch
            {
                // Ignore assemblies that can't be loaded
            }
        }
        
        return assembliesWithTickerQ.ToArray();
    }
    
    /// <summary>
    /// Loads assemblies from a directory that contain TickerQ functions.
    /// The ModuleInitializer will auto-initialize them upon loading.
    /// </summary>
    public static Assembly[] LoadTickerQAssembliesFromDirectory(string directoryPath, SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        
        var assemblies = new List<Assembly>();
        var dllFiles = Directory.GetFiles(directoryPath, "*.dll", searchOption);
        
        foreach (var dllPath in dllFiles)
        {
            try
            {
                // LoadFrom will trigger the ModuleInitializer
                var assembly = Assembly.LoadFrom(dllPath);
                if (HasTickerQGeneratedClass(assembly))
                {
                    assemblies.Add(assembly);
                }
            }
            catch (Exception ex)
            {
                // Log or handle assembly loading errors
                // Silently skip assemblies that can't be loaded or don't have TickerQ
                System.Diagnostics.Debug.WriteLine($"Could not load assembly {dllPath}: {ex.Message}");
            }
        }
        
        return assemblies.ToArray();
    }
    
    /// <summary>
    /// Checks if an assembly contains the TickerQ-generated class.
    /// This is more performant than checking all types for attributes.
    /// </summary>
    private static bool HasTickerQGeneratedClass(Assembly assembly)
    {
        try
        {
            // Skip TickerQ core assembly itself
            if (assembly.GetName().Name == "TickerQ")
                return false;
            
            // The source generator creates a class named "{AssemblyName}.TickerQInstanceFactoryExtensions"
            var generatedTypeName = $"{assembly.GetName().Name}.{GeneratedClassSuffix}";
            var generatedType = assembly.GetType(generatedTypeName, false);
            
            if (generatedType != null)
            {
                // Verify it has the Initialize method with ModuleInitializer attribute
                var initMethod = generatedType.GetMethod("Initialize", 
                    BindingFlags.Public | BindingFlags.Static);
                
                if (initMethod != null)
                {
                    // Check for ModuleInitializer attribute to ensure it's a genuine TickerQ class
                    var hasModuleInitializer = initMethod.GetCustomAttribute<ModuleInitializerAttribute>() != null;
                    return hasModuleInitializer;
                }
            }
            
            return false;
        }
        catch
        {
            return false;
        }
    }
}
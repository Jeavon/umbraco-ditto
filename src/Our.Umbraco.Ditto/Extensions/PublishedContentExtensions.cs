﻿namespace Our.Umbraco.Ditto
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;
    using System.Web;
    using System.Web.Mvc;

    using global::Umbraco.Core;
    using global::Umbraco.Core.Models;
    using global::Umbraco.Web;

    /// <summary>
    /// Encapsulates extension methods for <see cref="IPublishedContent"/>.
    /// </summary>
    public static class PublishedContentExtensions
    {
        /// <summary>
        /// The cache for storing constructor parameter information.
        /// </summary>
        private static readonly ConcurrentDictionary<Type, ParameterInfo[]> ConstructorCache
            = new ConcurrentDictionary<Type, ParameterInfo[]>();

        /// <summary>
        /// The cache for storing type property information.
        /// </summary>
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache
            = new ConcurrentDictionary<Type, PropertyInfo[]>();

        /// <summary>
        /// Returns the given instance of <see cref="IPublishedContent"/> as the specified type.
        /// </summary>
        /// <param name="content">
        /// The <see cref="IPublishedContent"/> to convert.
        /// </param>
        /// <param name="convertingType">
        /// The <see cref="Action{ConvertingTypeEventArgs}"/> to fire when converting.
        /// </param>
        /// <param name="convertedType">
        /// The <see cref="Action{ConvertedTypeEventArgs}"/> to fire when converted.
        /// </param>
        /// <typeparam name="T">
        /// The <see cref="Type"/> of items to return.
        /// </typeparam>
        /// <returns>
        /// The resolved <see cref="T"/>.
        /// </returns>
        public static T As<T>(
            this IPublishedContent content,
            Action<ConvertingTypeEventArgs> convertingType = null,
            Action<ConvertedTypeEventArgs> convertedType = null,
            CultureInfo culture = null)
            where T : class
        {
            return content.As(typeof(T), convertingType, convertedType, culture) as T;
        }

        /// <summary>
        /// Gets a collection of the given type from the given <see cref="IEnumerable{IPublishedContent}"/>.
        /// </summary>
        /// <param name="items">
        /// The <see cref="IEnumerable{IPublishedContent}"/> to convert.
        /// </param>
        /// <param name="documentTypeAlias">
        /// The document type alias.
        /// </param>
        /// <param name="convertingType">
        /// The <see cref="Action{ConvertingTypeEventArgs}"/> to fire when converting.
        /// </param>
        /// <param name="convertedType">
        /// The <see cref="Action{ConvertedTypeEventArgs}"/> to fire when converted.
        /// </param>
        /// <typeparam name="T">
        /// The <see cref="Type"/> of items to return.
        /// </typeparam>
        /// <returns>
        /// The resolved <see cref="IEnumerable{T}"/>.
        /// </returns>
        public static IEnumerable<T> As<T>(
            this IEnumerable<IPublishedContent> items,
            string documentTypeAlias = null,
            Action<ConvertingTypeEventArgs> convertingType = null,
            Action<ConvertedTypeEventArgs> convertedType = null,
            CultureInfo culture = null)
            where T : class
        {
            return items.As(typeof(T), documentTypeAlias, convertingType, convertedType, culture)
                .Select(x => x as T);
        }

        /// <summary>
        /// Returns an object representing the given <see cref="Type"/>.
        /// </summary>
        /// <param name="content">
        /// The <see cref="IPublishedContent"/> to convert.
        /// </param>
        /// <param name="type">
        /// The <see cref="Type"/> of items to return.
        /// </param>
        /// <param name="convertingType">
        /// The <see cref="Action{ConvertingTypeEventArgs}"/> to fire when converting.
        /// </param>
        /// <param name="convertedType">
        /// The <see cref="Action{ConvertedTypeEventArgs}"/> to fire when converted.
        /// </param>
        /// <returns>
        /// The converted <see cref="Object"/> as the given type.
        /// </returns>
        public static object As(
            this IPublishedContent content,
            Type type,
            Action<ConvertingTypeEventArgs> convertingType = null,
            Action<ConvertedTypeEventArgs> convertedType = null,
            CultureInfo culture = null)
        {
            if (content == null)
            {
                return null;
            }

            using (DisposableTimer.DebugDuration(type, string.Format("IPublishedContent As ({0})", content.DocumentTypeAlias), "Complete"))
            {
                // Check for and fire any event args
                var convertingArgs = new ConvertingTypeEventArgs
                {
                    Content = content
                };

                EventHandlers.CallConvertingTypeHandler(convertingArgs);

                if (!convertingArgs.Cancel && convertingType != null)
                {
                    convertingType(convertingArgs);
                }

                // Cancel if applicable. 
                if (convertingArgs.Cancel)
                {
                    return null;
                }

                // Create an object and fetch it as the type.
                object instance = GetTypedProperty(content, type, culture);

                // Fire the converted event
                var convertedArgs = new ConvertedTypeEventArgs
                {
                    Content = content,
                    Converted = instance,
                    ConvertedType = type
                };

                if (convertedType != null)
                {
                    convertedType(convertedArgs);
                }

                EventHandlers.CallConvertedTypeHandler(convertedArgs);

                return convertedArgs.Converted;
            }
        }

        /// <summary>
        /// Gets a collection of the given type from the given <see cref="IEnumerable{IPublishedContent}"/>.
        /// </summary>
        /// <param name="items">
        /// The <see cref="IEnumerable{IPublishedContent}"/> to convert.
        /// </param>
        /// <param name="type">
        /// The <see cref="Type"/> of items to return.
        /// </param>
        /// <param name="documentTypeAlias">
        /// The document type alias.
        /// </param>
        /// <param name="convertingType">
        /// The <see cref="Action{ConvertingTypeEventArgs}"/> to fire when converting.
        /// </param>
        /// <param name="convertedType">
        /// The <see cref="Action{ConvertedTypeEventArgs}"/> to fire when converted.
        /// </param>
        /// <returns>
        /// The resolved <see cref="IEnumerable{T}"/>.
        /// </returns>
        public static IEnumerable<object> As(
            this IEnumerable<IPublishedContent> items,
            Type type,
            string documentTypeAlias = null,
            Action<ConvertingTypeEventArgs> convertingType = null,
            Action<ConvertedTypeEventArgs> convertedType = null,
            CultureInfo culture = null)
        {
            using (DisposableTimer.DebugDuration<IEnumerable<object>>(string.Format("IEnumerable As ({0})", documentTypeAlias)))
            {
                if (string.IsNullOrWhiteSpace(documentTypeAlias))
                {
                    return items.Select(x => x.As(type, convertingType, convertedType, culture));
                }

                return items
                    .Where(x => documentTypeAlias.InvariantEquals(x.DocumentTypeAlias))
                    .Select(x => x.As(type, convertingType, convertedType, culture));
            }
        }

        /// <summary>
        /// Returns an object representing the given <see cref="Type"/>.
        /// </summary>
        /// <param name="content">
        /// The <see cref="IPublishedContent"/> to convert.
        /// </param>
        /// <param name="type">
        /// The <see cref="Type"/> of items to return.
        /// </param>
        /// <returns>
        /// The converted <see cref="Object"/> as the given type.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the given type has invalid constructors.
        /// </exception>
        private static object GetTypedProperty(
            IPublishedContent content,
            Type type,
            CultureInfo culture = null)
        {
            // Check if the culture has been set, otherwise use from Umbraco.
            if (culture == null)
            {
                culture = UmbracoContext.Current.PublishedContentRequest.Culture;
            }

            // Get the default constructor, parameters and create an instance of the type.
            // Try and return from the cache first. TryGetValue is faster than GetOrAdd.
            ParameterInfo[] constructorParams;
            ConstructorCache.TryGetValue(type, out constructorParams);

            if (constructorParams == null)
            {
                var constructor = type.GetConstructors().OrderBy(x => x.GetParameters().Length).First();
                constructorParams = constructor.GetParameters();
                ConstructorCache.TryAdd(type, constructorParams);
            }

            object instance;
            if (constructorParams.Length == 0)
            {
                // Internally this uses Activator.CreateInstance which is heavily optimized.
                instance = type.GetInstance();
            }
            else if (constructorParams.Length == 1 & constructorParams[0].ParameterType == typeof(IPublishedContent))
            {
                // This extension method is about 7x faster than the native implementation.
                instance = type.GetInstance(content);
            }
            else
            {
                throw new InvalidOperationException("Type {0} has invalid constructor parameters");
            }

            // Collect all the properties of the given type and loop through writable ones.
            PropertyInfo[] properties;
            PropertyCache.TryGetValue(type, out properties);
            if (properties == null)
            {
                properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(x => x.CanWrite).ToArray();
                PropertyCache.TryAdd(type, properties);
            }

            var contentType = content.GetType();

            foreach (var propertyInfo in properties)
            {
                using (DisposableTimer.DebugDuration(type, string.Format("ForEach Property ({1} {0})", propertyInfo.Name, content.Id), "Complete"))
                {
                    // Check for the ignore attribute.
                    var ignoreAttr = propertyInfo.GetCustomAttribute<DittoIgnoreAttribute>();
                    if (ignoreAttr != null)
                    {
                        continue;
                    }

                    var umbracoPropertyName = propertyInfo.Name;
                    var altUmbracoPropertyName = string.Empty;
                    var recursive = false;
                    object defaultValue = null;

                    var umbracoPropertyAttr = propertyInfo.GetCustomAttribute<UmbracoPropertyAttribute>();
                    if (umbracoPropertyAttr != null)
                    {
                        umbracoPropertyName = umbracoPropertyAttr.PropertyName;
                        altUmbracoPropertyName = umbracoPropertyAttr.AltPropertyName;
                        recursive = umbracoPropertyAttr.Recursive;
                        defaultValue = umbracoPropertyAttr.DefaultValue;
                    }

                    // Try fetching the value.
                    var contentProperty = contentType.GetProperty(umbracoPropertyName);
                    object propertyValue = contentProperty != null
                                            ? contentProperty.GetValue(content, null)
                                            : content.GetPropertyValue(umbracoPropertyName, recursive);

                    // Try fetching the alt value.
                    if ((propertyValue == null || propertyValue.ToString().IsNullOrWhiteSpace())
                        && !string.IsNullOrWhiteSpace(altUmbracoPropertyName))
                    {
                        contentProperty = contentType.GetProperty(altUmbracoPropertyName);
                        propertyValue = contentProperty != null
                                            ? contentProperty.GetValue(content, null)
                                            : content.GetPropertyValue(altUmbracoPropertyName, recursive);
                    }

                    // Try setting the default value.
                    if ((propertyValue == null || propertyValue.ToString().IsNullOrWhiteSpace())
                        && defaultValue != null)
                    {
                        propertyValue = defaultValue;
                    }

                    // Process the value.
                    if (propertyValue != null)
                    {
                        var propertyType = propertyInfo.PropertyType;
                        var typeInfo = propertyType.GetTypeInfo();
                        var isEnumerableType = propertyType.IsEnumerableType() && typeInfo.GenericTypeArguments.Any();

                        // Try any custom type converters first.
                        // 1: Check the property.
                        // 2: Check any type arguments in generic enumerable types.
                        // 3: Check the type itself.
                        var converterAttribute =
                            propertyInfo.GetCustomAttribute<TypeConverterAttribute>()
                            ?? (isEnumerableType ? typeInfo.GenericTypeArguments.First().GetCustomAttribute<TypeConverterAttribute>(true)
                                                 : propertyType.GetCustomAttribute<TypeConverterAttribute>(true));

                        if (converterAttribute != null)
                        {
                            if (converterAttribute.ConverterTypeName != null)
                            {
                                // Time custom conversions.
                                using (DisposableTimer.DebugDuration(type, string.Format("Custom TypeConverter ({0}, {1})", content.Id, propertyInfo.Name), "Complete"))
                                {
                                    // Get the custom converter from the attribute and attempt to convert.
                                    var toConvert = Type.GetType(converterAttribute.ConverterTypeName);
                                    if (toConvert != null)
                                    {
                                        var converter = DependencyResolver.Current.GetService(toConvert) as TypeConverter;
                                        if (converter != null && converter.CanConvertFrom(propertyValue.GetType()))
                                        {
                                            // Create context to pass to converter implementations.
                                            // This contains the IPublishedContent and the currently converting property descriptor.
                                            var descriptor = TypeDescriptor.GetProperties(instance)[propertyInfo.Name];
                                            var context = new PublishedContentContext(content, descriptor);
                                            object converted = converter.ConvertFrom(context, culture, propertyValue);

                                            // Handle Typeconverters returning single objects when we want an IEnumerable.
                                            // Use case: Someone selects a folder of images rather than a single image with the media picker.
                                            if (isEnumerableType)
                                            {
                                                var parameterType = typeInfo.GenericTypeArguments.First();

                                                // Some converters return an IEnumerable so we check again.
                                                if (!converted.GetType().IsEnumerableType())
                                                {
                                                    // Generate a method using 'Cast' to convert the type back to IEnumerable<T>.
                                                    MethodInfo castMethod = typeof(Enumerable).GetMethod("Cast").MakeGenericMethod(parameterType);
                                                    object enumerablePropertyValue = castMethod.Invoke(null, new object[] { converted.YieldSingleItem() });
                                                    propertyInfo.SetValue(instance, enumerablePropertyValue, null);
                                                }
                                                else
                                                {
                                                    propertyInfo.SetValue(instance, converted, null);
                                                }
                                            }
                                            else
                                            {
                                                propertyInfo.SetValue(instance, converted, null);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        else if (propertyInfo.PropertyType == typeof(HtmlString))
                        {
                            // Handle Html strings so we don't have to set the attribute.
                            HtmlStringConverter converter = new HtmlStringConverter();
                            if (converter.CanConvertFrom(propertyValue.GetType()))
                            {
                                // This contains the IPublishedContent and the currently converting property descriptor.
                                var descriptor = TypeDescriptor.GetProperties(instance)[propertyInfo.Name];
                                var context = new PublishedContentContext(content, descriptor);
                                propertyInfo.SetValue(instance, converter.ConvertFrom(context, culture, propertyValue), null);
                            }
                        }
                        else if (propertyInfo.PropertyType.IsInstanceOfType(propertyValue))
                        {
                            // Simple types
                            propertyInfo.SetValue(instance, propertyValue, null);
                        }
                        else
                        {
                            using (DisposableTimer.DebugDuration(type, string.Format("TypeConverter ({0}, {1})", content.Id, propertyInfo.Name), "Complete"))
                            {
                                var convert = propertyValue.TryConvertTo(propertyInfo.PropertyType);
                                if (convert.Success)
                                {
                                    propertyInfo.SetValue(instance, convert.Result, null);
                                }
                            }
                        }
                    }
                }
            }

            return instance;
        }
    }
}
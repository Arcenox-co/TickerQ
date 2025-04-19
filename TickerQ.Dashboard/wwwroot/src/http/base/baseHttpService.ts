// useBaseHttpService.ts

import axios, {type Method } from "axios";
import {type Ref, ref } from "vue";
import http from "./axiosConfig";
import type { Path, PathValue } from "@/utilities/pathTypes";

/* ------------------------------------------------------------------
   1) Define TableHeader Interface and Helper Function
------------------------------------------------------------------ */

/**
 * Describes the structure of each table header.
 */
export interface TableHeader {
  title: string;
  key: string;
  align?: 'start' | 'center' | 'end';
  sortable?: boolean;
  visibility: boolean;
  // Add any additional properties as needed
}

/**
 * Converts a camelCase or snake_case string to Title Case.
 * E.g., 'firstName' -> 'First Name', 'last_name' -> 'Last Name'
 */
function formatKeyToTitle(key: string): string {
  // Replace underscores with spaces
  let result = key.replace(/_/g, ' ');
  
  // Insert spaces before capital letters (for camelCase)
  result = result.replace(/([A-Z])/g, ' $1');
  
  // Capitalize the first letter of each word
  return result.replace(/\b\w/g, char => char.toUpperCase()).trim();
}

/* ------------------------------------------------------------------
   2) Define Service Interfaces
------------------------------------------------------------------ */

/** Single-response interface */
export interface BaseHttpServiceSingle<TRequest, TSingle extends object> {
  loader: Ref<boolean>;
  response: Ref<TSingle | undefined>;
  headers: Ref<TableHeader[] | undefined>;
  updateResponse(response: TSingle | undefined): void;
  updateProperty<T extends Path<TSingle>>(key: T, keyValue: PathValue<TSingle, T>): void;

  sendAsync(
    methodType: Method,
    url: string,
    options?: {
      bodyData?: TRequest;
      paramData?: Record<string, any>;
    }
  ): Promise<TSingle>;

  /**
   * Called if you want to "filter" or "transform" the server response
   * to match the shape of `TSingle`. 
   * 
   * For single-mode, TModel = TSingle.
   */
  FixToResponseModel(
    model: new () => TSingle,
    transform?: (item: TSingle) => TSingle
  ): this;

  /**
   * Generate or adjust table headers based on the model's keys.
   * The transform function operates on individual TableHeader items.
   * Must be called after FixToResponseModel.
   */
  FixToHeaders(
    transform?: (header: TableHeader) => TableHeader
  ): this;
}

/** Array-response interface */
export interface BaseHttpServiceArray<TRequest, TItem extends object> {
  loader: Ref<boolean>;
  response: Ref<TItem[] | undefined>;
  headers: Ref<TableHeader[] | undefined>;
  updateResponse(newResponse: TItem[] | undefined): void;
  addToResponse(newItem: TItem): void;
  removeFromResponse<T extends Path<TItem>>(key: T, value: PathValue<TItem, T>): void;
  updateByKey<T extends Path<TItem>>(key: T, value: TItem, ignoreKeys: T[]): void;
  updatePropertyByKey<T extends Path<TItem>, V extends Path<TItem>>(key: T, keyValue: PathValue<TItem, T>, property: V, value: PathValue<TItem, V>): void;
  updateProperty<T extends Path<TItem>>(key: T, keyValue: PathValue<TItem, T>): void;

  sendAsync(
    methodType: Method,
    url: string,
    options?: {
      bodyData?: TRequest;
      paramData?: Record<string, any>;
    }
  ): Promise<TItem[]>;

  /**
   * For array-mode, you pass a model describing a single item `TItem`.
   * Then optionally transform each item.
   */
  FixToResponseModel(
    model: new () => TItem,
    transform?: (item: TItem) => TItem
  ): this;

  /**
   * Generate or adjust table headers based on the model's keys.
   * The transform function operates on individual TableHeader items.
   * Must be called after FixToResponseModel.
   */
  FixToHeaders(
    transform?: (header: TableHeader) => TableHeader
  ): this;

  ReOrganizeResponse(
    transform: (response: TItem[]) => TItem[] | undefined
  ): this;
}

/* ------------------------------------------------------------------
   3) Overloads: "single" vs "array"
------------------------------------------------------------------ */

/**
 * Initializes the HTTP service in "single" mode.
 * @param mode - Must be "single".
 * @returns A service tailored for single-item responses.
 */
export function useBaseHttpService<TRequest extends object, TSingle extends object>(
  mode: "single"
): BaseHttpServiceSingle<TRequest, TSingle>;

/**
 * Initializes the HTTP service in "array" mode.
 * @param mode - Must be "array".
 * @returns A service tailored for array responses.
 */
export function useBaseHttpService<TRequest extends object, TItem extends object>(
  mode: "array"
): BaseHttpServiceArray<TRequest, TItem>;

/* ------------------------------------------------------------------
   4) Implementation of useBaseHttpService
------------------------------------------------------------------ */

/**
 * Factory function to create an HTTP service tailored for single or array responses.
 * @param mode - "single" or "array".
 * @returns An instance of BaseHttpServiceSingle or BaseHttpServiceArray.
 */
export function useBaseHttpService(
  mode: "single" | "array"
): BaseHttpServiceSingle<any, any> | BaseHttpServiceArray<any, any> {

  const cancelRequest: Ref<Function | undefined> = ref(undefined);
  const loader = ref(false);

  // Will be set if you call FixToResponseModel
  const responseModelKeys: Ref<string[] | undefined> = ref([]);
  let transformFn: ((item: any) => any) | undefined;
  let reOrganizeFn: ((response: any) => any) | undefined;

  /**
   * A helper to process raw data from the server:
   * - Filter to `responseModelKeys` (case-insensitive)
   * - Then apply `transformFn` if present
   */
  function processResponse<T extends object>(
    data: any,
    keys?: string[],
    transform?: (x: T) => T
  ): T | T[] {
    if (Array.isArray(data)) {
      return data.map((item) => processOneItem<T>(item, keys, transform));
    }
    return processOneItem<T>(data, keys, transform);
  }

  function processOneItem<T extends object>(
    item: any,
    keys?: string[],
    transform?: (x: T) => T
  ): T {
    if (!item || typeof item !== "object") {
      return {} as T;
    }

    let filtered: Partial<T>;
    if (keys && keys.length > 0) {
      filtered = {};
      // For each model key (case-insensitive), find the property in `item`
      for (const modelKey of keys) {
        const foundItemKey = Object.keys(item).find(
          (k) => k.toLowerCase() === modelKey.toLowerCase()
        );
        if (foundItemKey) {
          filtered[modelKey as keyof T] = item[foundItemKey];
        }
      }
    } else {
      // If no keys specified, clone entire item
      filtered = { ...item };
    }

    return transform ? transform(filtered as T) : (filtered as T);
  }

  /* --------------------------------------------------------------
     "single" mode
     -------------------------------------------------------------- */
  if (mode === "single") {
    const response = ref<any>();
    const headers = ref<TableHeader[] | undefined>(undefined);

    const sendAsync = async (
      methodType: Method,
      url: string,
      options?: { bodyData?: any; paramData?: Record<string, any> }
    ): Promise<any> => {
      if (cancelRequest.value) cancelRequest.value();
      loader.value = true;
      try {
        const res = await http.request({
          url,
          method: methodType,
          params: options?.paramData,
          headers: { 'Content-Type': 'application/json' },
          data: options?.bodyData,
          cancelToken: new axios.CancelToken((exec) => {
            cancelRequest.value = exec;
          }),
        });

        const processed = processResponse(res.data, responseModelKeys.value, transformFn);

        if(reOrganizeFn) {
          response.value = reOrganizeFn(processed) as any;
        } else {
          response.value = processed as any;
        }

        return response.value;
      } finally {
        loader.value = false;
      }
    };

    const FixToResponseModel = (model: new () => any, transform?: (item: any) => any) => {
      responseModelKeys.value = Object.keys(new model());
      transformFn = transform;
      return baseHttpService;
    };

    const updateResponse = (newResponse: any) => {
      const processed = processResponse(newResponse, responseModelKeys.value, transformFn);
      response.value = processed;
    };

    /**
     * FixToHeaders
     * Derive default headers from the model's keys,
     * optionally transform them individually.
     * Must be called after FixToResponseModel.
     */
    const FixToHeaders = (
      transform?: (header: TableHeader) => TableHeader
    ) => {
      const keys = responseModelKeys.value;
      if (!keys) {
        console.warn("FixToHeaders called before FixToResponseModel.");
        return baseHttpService;
      }

      let defaultHeaders: TableHeader[] = keys.map((key) => ({
        title: formatKeyToTitle(key),
        key,
        sortable: true,
        visibility: true
      }));

      // Apply transform to each header if provided
      if (typeof transform === "function") {
        defaultHeaders = defaultHeaders.map(transform);
        defaultHeaders = defaultHeaders.filter(x => x.visibility == true);
      }

      headers.value = defaultHeaders;
      return baseHttpService;
    };
    

    const updateProperty = (property: string, value: any) => {
        response.value[property] = value;
    };

    const baseHttpService: BaseHttpServiceSingle<any, any> = {
      loader,
      response,
      headers,
      sendAsync,
      FixToResponseModel,
      FixToHeaders,
      updateResponse,
      updateProperty
    };

    return baseHttpService;
  }

  /* --------------------------------------------------------------
     "array" mode
     -------------------------------------------------------------- */
  else {
    const response = ref<any[]>();
    const headers = ref<TableHeader[] | undefined>(undefined);

    const sendAsync = async (
      methodType: Method,
      url: string,
      options?: { bodyData?: any; paramData?: Record<string, any> }
    ): Promise<any[]> => {
      if (cancelRequest.value) cancelRequest.value(); // Cancel existing request
      loader.value = true;
      try {
        const res = await http.request<any[]>({
          url,
          method: methodType,
          params: options?.paramData,
          data: options?.bodyData,
          headers: { 'Content-Type': 'application/json' },
          cancelToken: new axios.CancelToken((exec) => {
            cancelRequest.value = exec;
          }),
        });

        const organized = reOrganizeFn ? reOrganizeFn(res.data) : res.data;

        const processed = processResponse(organized, responseModelKeys.value, transformFn);
        
        response.value = processed as any[];

        return response.value;

      } finally {
        loader.value = false;
      }
    };

    const FixToResponseModel = (model: new () => any, transform?: (item: any) => any) => {
      responseModelKeys.value = Object.keys(new model());
      transformFn = transform;
      return baseHttpService;
    };

    const updateResponse = (newResponse: any[] | undefined) => {
      if(reOrganizeFn) {
        newResponse = reOrganizeFn(newResponse);
      }
      const processed = processResponse(newResponse, responseModelKeys.value, transformFn);
      response.value = processed;
    };

    const addToResponse = (newItem: any) => {
      const processed = processResponse(newItem, responseModelKeys.value, transformFn);
      response.value?.push(processed);
      if(reOrganizeFn) {
        response.value = reOrganizeFn(response.value);
      }
    };

    const removeFromResponse = (key: any, value: any) => {
      response.value = response.value?.filter(item => item[key] !== value);
    };

    const updateByKey = (key: string, value: any, ignoreKeys: string[] = []) => {
      const item = response.value?.find(item => item[key] == value[key]);
      const processed = processResponse(value, responseModelKeys.value, transformFn);

      Object.keys(processed).forEach((itemKey) => {
        if(!ignoreKeys.includes(itemKey)) {
          item[itemKey] = processed[itemKey];
        }
      });

      if(reOrganizeFn) {
        response.value = reOrganizeFn(response.value);
      }
    };

    const updatePropertyByKey = (key: string, keyValue: any, property: string, value: any) => {
      const item = response.value?.find(item => item[key] === keyValue);
      if (item) {
        item[property] = value;
      }
    };

    const updateProperty = (property: string, value: any) => {
      response.value?.forEach(x => {
        x[property] = value
      })
  };

    const ReOrganizeResponse = (transform: (response: any[]) => any[]) => {
      reOrganizeFn = transform;
      return baseHttpService;
    };
    
    /**
     * FixToHeaders
     * Derive default headers from the model's keys,
     * optionally transform them individually.
     * Must be called after FixToResponseModel.
     */
    const FixToHeaders = (
      transform?: (header: TableHeader) => TableHeader
    ) => {
      const keys = responseModelKeys.value;
      if (!keys) {
        console.warn("FixToHeaders called before FixToResponseModel.");
        return baseHttpService;
      }

      let defaultHeaders: TableHeader[] = keys.map((key) => ({
        title: formatKeyToTitle(key),
        key,
        sortable: true,
        visibility: true
      }));

      // Apply transform to each header if provided
      if (typeof transform === "function") {
        defaultHeaders = defaultHeaders.map(transform);
        defaultHeaders = defaultHeaders.filter(x => x.visibility == true);
      }

      headers.value = defaultHeaders;
      return baseHttpService;
    };

    const baseHttpService: BaseHttpServiceArray<any, any> = {
      loader,
      response,
      headers,
      sendAsync,
      FixToResponseModel,
      FixToHeaders,
      updateResponse,
      addToResponse,
      removeFromResponse,
      updateByKey,
      ReOrganizeResponse,
      updatePropertyByKey,
      updateProperty
    };

    return baseHttpService;
  }
}
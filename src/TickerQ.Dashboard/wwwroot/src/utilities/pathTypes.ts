export type Path<T> = T extends object
  ? {
    [K in keyof T & string]: T[K] extends object
    ? `${K}` | `${K}.${Path<T[K]>}`
    : `${K}`;
  }[keyof T & string]
  : never;

export type PathWithSuffix<T> = T extends object
  ? {
    [K in keyof T & string]: T[K] extends object
    ? `${K}` | `${K}.${Path<T[K]>}` | `${K}__blur`
    : `${K}` | `${K}__blur`;
  }[keyof T & string]
  : never;

export type PathValue<T, P extends PathWithSuffix<T>> =
  P extends `${infer K}__blur`
  ? K extends keyof T
  ? T[K]
  : never
  : P extends `${infer K}.${infer Rest}`
  ? K extends keyof T
  ? Rest extends PathWithSuffix<T[K]>
  ? PathValue<T[K], Rest>
  : never
  : never
  : P extends keyof T
  ? T[P]
  : never;
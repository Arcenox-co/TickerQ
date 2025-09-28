export enum Status {
    Idle = 0,
    Queued = 1,
    InProgress = 2,
    Done = 3,
    DueDone = 4,
    Failed = 5,
    Cancelled = 6,
    Skipped = 7,
  }

  export const getStatusValueSafe = (statusString: string | number): number => {
    // If it's already a number, return it
    if (typeof statusString === 'number') return statusString;
    
    // Get all enum keys and find case-insensitive match
    const enumKeys = Object.keys(Status).filter(key => isNaN(Number(key)));
    const matchedKey = enumKeys.find(key => 
      key.toLowerCase() === statusString.toLowerCase()
    );
    
    if (matchedKey) {
      return Status[matchedKey as keyof typeof Status];
    }
    
    return Status.Failed; // Default fallback
  };
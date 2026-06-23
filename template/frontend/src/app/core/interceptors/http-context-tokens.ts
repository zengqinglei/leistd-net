import { HttpContextToken } from '@angular/common/http';

export const SILENT_AUTH = new HttpContextToken<boolean>(() => false);

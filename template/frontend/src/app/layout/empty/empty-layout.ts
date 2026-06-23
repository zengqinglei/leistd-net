import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { DefaultFooter } from '../components/default-footer/default-footer';

@Component({
  selector: 'app-empty-layout',
  standalone: true,
  imports: [RouterOutlet, DefaultFooter],
  templateUrl: './empty-layout.html'
})
export class EmptyLayout {}

import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HlmButtonImports } from '@spartan-ng/helm/button';

@Component({
  imports: [RouterOutlet, HlmButtonImports],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {}

import { Injectable, inject } from '@angular/core';
import type { OperationVariables, TypedDocumentNode } from '@apollo/client';
import { Apollo } from 'apollo-angular';
import { Observable, filter, firstValueFrom, map } from 'rxjs';
import { ApiError } from '../api/rest';

/** Promise-based access to the GraphQL API with errors turned into ApiError codes. */
@Injectable({ providedIn: 'root' })
export class Gql {
  private readonly apollo = inject(Apollo);

  async query<TData, TVariables extends OperationVariables>(
    document: TypedDocumentNode<TData, TVariables>,
    variables?: TVariables,
  ): Promise<TData> {
    try {
      const result = await firstValueFrom(
        this.apollo.query<TData, TVariables>({ query: document, variables } as never),
      );
      return result.data as TData;
    } catch (error) {
      throw ApiError.from(error);
    }
  }

  async mutate<TData, TVariables extends OperationVariables>(
    document: TypedDocumentNode<TData, TVariables>,
    variables?: TVariables,
  ): Promise<TData> {
    try {
      const result = await firstValueFrom(
        this.apollo.mutate<TData, TVariables>({ mutation: document, variables } as never),
      );
      return result.data as TData;
    } catch (error) {
      throw ApiError.from(error);
    }
  }

  subscribe<TData, TVariables extends OperationVariables>(
    document: TypedDocumentNode<TData, TVariables>,
    variables?: TVariables,
  ): Observable<TData> {
    return this.apollo.subscribe<TData, TVariables>({ query: document, variables } as never).pipe(
      filter((result) => !!result.data),
      map((result) => result.data as TData),
    );
  }
}
